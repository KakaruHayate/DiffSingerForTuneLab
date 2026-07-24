using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiffSingerForTuneLab;

// 跨模型外部说话人嵌入懒读取器：voiceId → 三域 (acoustic/pitch/variance) emb 缓存。
//   每个 external voice 的 emb 从其自身包目录读取（非当前 A 包），由 Render 据 PartContext.CompatibleVoices 构建。
//   文件缺失（罕见，兼容包一般存在）→ 零向量兜底（该域不贡献，不崩）。
internal sealed class ExternalEmbSet
{
    readonly int mAcousticHidden;
    readonly int mPitchHidden;
    readonly int mVarianceHidden;
    readonly List<ExternalVoice> mVoiceEntries;
    readonly object mLock = new();
    readonly Dictionary<string, float[]> mAcousticCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, float[]> mPitchCache = new(StringComparer.Ordinal);
    readonly Dictionary<string, float[]> mVarianceCache = new(StringComparer.Ordinal);

    public ExternalEmbSet(int acousticHidden, int pitchHidden, int varianceHidden, IReadOnlyList<ExternalVoice> voices)
    {
        mAcousticHidden = acousticHidden;
        mPitchHidden = pitchHidden;
        mVarianceHidden = varianceHidden;
        mVoiceEntries = voices.ToList();
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
            if (mAcousticCache.TryGetValue(voiceId, out emb))
                return true;
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
            if (mPitchCache.TryGetValue(voiceId, out emb))
                return true;
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
            if (mVarianceCache.TryGetValue(voiceId, out emb))
                return true;
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

        // 先直接试 SpeakerEntry（acoustic）或 SpeakerEntry（predictor 同名惯例）。
        string direct = subdir is null
            ? Path.Combine(ext.RootPath, ext.SpeakerEntry + ".emb")
            : Path.Combine(ext.RootPath, subdir, ext.SpeakerEntry + ".emb");
        try
        {
            if (File.Exists(direct))
                return ReadEmbFile(direct, expectedHidden);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // predictor 子目录：按 suffix 在该子目录的 speakers 表里解析 entry（命名可能不同）。
        if (subdir is not null)
        {
            var cfgPath = Path.Combine(ext.RootPath, subdir, "dsconfig.yaml");
            if (File.Exists(cfgPath))
            {
                try
                {
                    var cfg = new DeserializerBuilder().Build()
                        .Deserialize<Dictionary<string, object?>>(File.ReadAllText(cfgPath));
                    string suffix = DiffSingerDeclarations.Suffix(ext.SpeakerEntry);
                    string? entry = cfg is not null && cfg.TryGetValue("speakers", out var sp) && sp is System.Collections.IEnumerable seq && sp is not string
                        ? seq.Cast<object?>().Select(x => x?.ToString())
                            .FirstOrDefault(s => !string.IsNullOrEmpty(s) && DiffSingerDeclarations.Suffix(s) == suffix)
                        : null;
                    if (entry is not null)
                    {
                        var alt = Path.Combine(ext.RootPath, subdir, entry + ".emb");
                        if (File.Exists(alt))
                            return ReadEmbFile(alt, expectedHidden);
                    }
                }
                catch { /* 解析失败回退零向量 */ }
            }
        }

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
