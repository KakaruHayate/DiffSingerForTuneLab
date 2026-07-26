using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;

namespace DiffSingerForTuneLab;

// 同模型跨 voicebank 的外部说话人嵌入懒读取器：voiceId → 三域 (acoustic/pitch/variance) emb 缓存。
//   每个 external voice 的 emb 从其自身包目录读取（非当前 A 包），由 Render 据 PartContext.CompatibleVoices 构建。
//   文件缺失（罕见，兼容包一般存在）→ 零向量兜底（该域不贡献，不崩）。
internal sealed class ExternalEmbSet
{
    readonly int mAcousticHidden;
    readonly int mPitchHidden;
    readonly int mVarianceHidden;
    readonly List<ExternalVoice> mVoiceEntries;
    readonly Action<string>? mWarn;   // 取不到 emb 时告警（生产传 ILogger.Warning；测试可省）
    readonly object mLock = new();
    readonly Dictionary<string, float[]> mAcousticCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, float[]> mPitchCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, float[]> mVarianceCache = new(StringComparer.Ordinal);

    public ExternalEmbSet(
        int acousticHidden, int pitchHidden, int varianceHidden,
        IReadOnlyList<ExternalVoice> voices, IReadOnlySet<string>? excludedVoiceIds = null,
        Action<string>? warn = null)
    {
        mAcousticHidden = acousticHidden;
        mPitchHidden = pitchHidden;
        mVarianceHidden = varianceHidden;
        mWarn = warn;
        mVoiceEntries = voices
            .Where(voice => excludedVoiceIds is null || !excludedVoiceIds.Contains(voice.VoiceId))
            .ToList();
    }

    // 仅当 voiceId 属于 mVoiceEntries 时才返回 true；否则返回 false 让调用方走原生解析器。
    public bool TryAcoustic(string voiceId, out float[] emb)
    {
        lock (mLock)
        {
            if (!IsExternal(voiceId))
            {
                emb = Array.Empty<float>();
                return false;
            }
            if (mAcousticCache.TryGetValue(voiceId, out var cached))
            {
                emb = cached;
                return true;
            }
            emb = ReadEmb(voiceId, subdir: null) ?? new float[mAcousticHidden];
            mAcousticCache[voiceId] = emb;
            return true;
        }
    }

    public bool TryPitch(string voiceId, out float[] emb)
    {
        lock (mLock)
        {
            if (!IsExternal(voiceId))
            {
                emb = Array.Empty<float>();
                return false;
            }
            if (mPitchCache.TryGetValue(voiceId, out var cached))
            {
                emb = cached;
                return true;
            }
            emb = ReadEmb(voiceId, "dspitch") ?? new float[mPitchHidden];
            mPitchCache[voiceId] = emb;
            return true;
        }
    }

    public bool TryVariance(string voiceId, out float[] emb)
    {
        lock (mLock)
        {
            if (!IsExternal(voiceId))
            {
                emb = Array.Empty<float>();
                return false;
            }
            if (mVarianceCache.TryGetValue(voiceId, out var cached))
            {
                emb = cached;
                return true;
            }
            emb = ReadEmb(voiceId, "dsvariance") ?? new float[mVarianceHidden];
            mVarianceCache[voiceId] = emb;
            return true;
        }
    }

    bool IsExternal(string voiceId)
    {
        foreach (var v in mVoiceEntries)
            if (v.VoiceId == voiceId)
                return true;
        return false;
    }

    float[]? ReadEmb(string voiceId, string? subdir)
    {
        var ext = mVoiceEntries.FirstOrDefault(v => v.VoiceId == voiceId);
        if (ext is null || string.IsNullOrEmpty(ext.RootPath))
            return null;

        int expectedHidden = subdir switch
        {
            null => mAcousticHidden,
            "dspitch" => mPitchHidden,
            "dsvariance" => mVarianceHidden,
            _ => mAcousticHidden,
        };

        // 该域的实际目录：acoustic = 包根，predictor = 对应子目录。两者结构同形（各自一份 dsconfig.yaml + .emb），
        //   故解析逻辑三域共用：先试 SpeakerEntry 直取，再按 suffix 经该域 speakers 表反查真实 entry。
        string dir = subdir is null ? ext.RootPath : Path.Combine(ext.RootPath, subdir);

        // 直取：SpeakerEntry 已是完整 entry 名（legacy 包即如此）时命中。
        string direct = Path.Combine(dir, ext.SpeakerEntry + ".emb");
        try
        {
            if (File.Exists(direct))
                return ReadEmbFile(direct, expectedHidden);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // 表反查：SpeakerEntry 是 dsconfig 后缀（manifest 包的 voices[].speaker 语义）时，
        //   文件名为「前缀.后缀」，直取必然落空——须按 suffix 在该域 speakers 表里找回完整 entry。
        //   与原生 DiffSingerModels.GetSpeakerEmbeddingBySuffix 的解析语义对齐；缺了它 acoustic 域会静默返回零向量。
        string suffix = DiffSingerDeclarations.Suffix(ext.SpeakerEntry);
        var cfgPath = Path.Combine(dir, "dsconfig.yaml");
        if (File.Exists(cfgPath))
        {
            try
            {
                var cfg = new DeserializerBuilder().Build()
                    .Deserialize<Dictionary<string, object?>>(File.ReadAllText(cfgPath));
                string? entry = cfg is not null && cfg.TryGetValue("speakers", out var sp) && sp is System.Collections.IEnumerable seq && sp is not string
                    ? seq.Cast<object?>().Select(x => x?.ToString())
                        .FirstOrDefault(s => !string.IsNullOrEmpty(s) && DiffSingerDeclarations.Suffix(s) == suffix)
                    : null;
                if (entry is not null)
                {
                    var alt = Path.Combine(dir, entry + ".emb");
                    if (File.Exists(alt))
                        return ReadEmbFile(alt, expectedHidden);
                }
            }
            catch { /* 解析失败落到下方告警 + 零向量 */ }
        }

        // 走到这里 = 该 voice 确属外部候选（指纹已匹配）却取不到本域 emb。零向量会被照常按权重计入归一化分母，
        //   听感是目标音色被稀释而非缺失——静默失败极难排查，故告警。
        mWarn?.Invoke($"DiffSinger：外部说话人 {ext.VoiceId} 的 {subdir ?? "acoustic"} 域 .emb 未找到"
            + $"（{dir}，entry/后缀 {ext.SpeakerEntry}），该域按零向量处理");
        return null;
    }

    static float[] ReadEmbFile(string path, int expectedHidden)
    {
        var bytes = File.ReadAllBytes(path);
        int count = Math.Min(bytes.Length / 4, expectedHidden);
        var emb = new float[expectedHidden];
        for (int i = 0; i < count; i++)
            emb[i] = BitConverter.ToSingle(bytes, i * 4);
        // count < expectedHidden → 尾部自然为 0（防御性，兼容包一般等长）。
        return emb;
    }
}
