using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using YamlDotNet.Serialization;

namespace DiffSingerForTuneLab;

// 一个声库包的「模型身份」：全部非声码器 ONNX 文件内容哈希（XxHash64）构成的有序元组。
//   两包指纹严格全等 ⟺ 非声码器 ONNX 逐一相同（含存在/不存在一致），即「同一模型、可互混 emb」。
//   参与指纹：acoustic + 三个固定 predictor 槽位各自的 linguistic + role。
//   子目录缺失或 dsconfig 无该 role → 对应槽位写 MissingSlot，确保不同结构不会坍缩成相同序列。
//   声码器 **不** 参与（已有 mel_base 变换兜底；vocoder 可不同）。
//   磁盘缓存于 UserDataRoot/Cache/fingerprints.json（key = rootPath）。
public readonly struct ModelFingerprint : IEquatable<ModelFingerprint>
{
    internal const int SlotCount = 7;
    internal const ulong MissingSlot = ulong.MaxValue;

    // 有序哈希数组。顺序 = [acoustic, dsdur-ling, dsdur-dur, dspitch-ling, dspitch-pitch, dsvariance-ling, dsvariance-variance]；
    //   子目录不存在 / role 字段缺失 → 对应位置写入 MissingSlot；有效指纹恒为 SlotCount 个固定槽位。
    readonly IReadOnlyList<ulong>? mHashes;
    public IReadOnlyList<ulong> Hashes => mHashes ?? Array.Empty<ulong>();

    public ModelFingerprint(IReadOnlyList<ulong> hashes)
        => mHashes = hashes ?? throw new ArgumentNullException(nameof(hashes));

    public static bool operator ==(ModelFingerprint a, ModelFingerprint b)
    {
        if (a.Hashes.Count != b.Hashes.Count) return false;
        for (int i = 0; i < a.Hashes.Count; i++)
            if (a.Hashes[i] != b.Hashes[i]) return false;
        return true;
    }
    public static bool operator !=(ModelFingerprint a, ModelFingerprint b) => !(a == b);
    public override int GetHashCode()
    {
        int h = HashCode.Combine(Hashes.Count);
        foreach (var v in Hashes) h = HashCode.Combine(h, (int)(v ^ (v >> 32)));
        return h;
    }
    public bool Equals(ModelFingerprint other) => this == other;
    public override bool Equals(object? obj) => obj is ModelFingerprint f && this == f;
    public override string ToString() => $"Fingerprint({Hashes.Count} hashes)";

    // 计算一个声库包的指纹（只读 ONNX，不加载会话）。
    // hashFn: 文件路径 → XxHash64 值（生产用 DiffSingerTensorCache.HashFile，测试可用固定值）。
    // warn: 可选的警告回调（生产用 ILogger.Warning）。
    public static ModelFingerprint Compute(string rootPath, VoicebankConfig config, Func<string, ulong> hashFn, Action<string>? warn = null)
    {
        var hashes = new List<ulong>();
        AddHash(rootPath, config.AcousticFileName, hashes, hashFn);
        foreach (var subdir in new[] { "dsdur", "dspitch", "dsvariance" })
        {
            var dir = Path.Combine(rootPath, subdir);
            var cfgPath = Path.Combine(dir, "dsconfig.yaml");
            if (!File.Exists(cfgPath))
            {
                hashes.Add(MissingSlot);
                hashes.Add(MissingSlot);
                continue;
            }
            var cfg = ReadYaml(cfgPath, warn);
            if (cfg is null)
                throw new InvalidOperationException($"Cannot parse fingerprint config: {cfgPath}");
            string lingFile = GetString(cfg, "linguistic");
            AddOptionalHash(dir, lingFile, hashes, hashFn);
            string role = PredictorRole(subdir);
            string roleFile = GetString(cfg, role);
            AddOptionalHash(dir, roleFile, hashes, hashFn);
        }
        return new ModelFingerprint(hashes);
    }

    static void AddOptionalHash(
        string dir, string fileName, List<ulong> hashes, Func<string, ulong> hashFn)
    {
        if (string.IsNullOrEmpty(fileName))
            hashes.Add(MissingSlot);
        else
            AddHash(dir, fileName, hashes, hashFn);
    }

    static void AddHash(string dir, string fileName, List<ulong> hashes, Func<string, ulong> hashFn)
    {
        var path = Path.Combine(dir, fileName);
        try { hashes.Add(hashFn(path)); }
        catch (Exception ex) { throw new InvalidOperationException($"指纹计算失败：{path}: {ex.Message}", ex); }
    }

    static string PredictorRole(string subdir) => subdir switch
    {
        "dsdur" => "dur", "dspitch" => "pitch", "dsvariance" => "variance", _ => string.Empty,
    };

    public static IReadOnlyDictionary<string, object?>? ReadYaml(string path, Action<string>? warn = null)
    {
        try { return new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(File.ReadAllText(path)); }
        catch (Exception ex) { warn?.Invoke($"DiffSinger：解析指纹用配置失败 {path}: {ex.Message}"); return null; }
    }

    public static string GetString(IReadOnlyDictionary<string, object?> map, string key)
        => map.TryGetValue(key, out var v) && v is string s ? s : string.Empty;
}

// 磁盘缓存（fingerprints.json）：rootPath → 条目。磁盘缓存跨会话加速首次加载。
public static class FingerprintCache
{
    const int CacheVersion = 3;
    public static string CachePath => Path.Combine(DiffSingerTensorCache.CacheDirectory, "fingerprints.json");

    sealed class EntryDto
    {
        public int version { get; set; }
        public List<long>? sizes { get; set; }
        public List<long>? mtimes { get; set; }
        public List<ulong>? hashes { get; set; }
        public long ts { get; set; }
    }

    public static Dictionary<string, FingerprintEntry> Load(Action<string>? warn = null)
    {
        if (!File.Exists(CachePath))
            return new Dictionary<string, FingerprintEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var yaml = new DeserializerBuilder().Build()
                .Deserialize<Dictionary<string, EntryDto>>(File.ReadAllText(CachePath));
            var result = new Dictionary<string, FingerprintEntry>(StringComparer.OrdinalIgnoreCase);
            if (yaml is not null)
                foreach (var kv in yaml)
                    if (kv.Value is { version: CacheVersion, hashes: { Count: ModelFingerprint.SlotCount } })
                        result[kv.Key] = new FingerprintEntry(kv.Value.sizes ?? [], kv.Value.mtimes ?? [],
                            new ModelFingerprint(kv.Value.hashes), kv.Value.ts);
            return result;
        }
        catch (Exception ex)
        {
            warn?.Invoke($"DiffSinger：读取指纹缓存失败（将重建）: {ex.Message}");
            return new Dictionary<string, FingerprintEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void Save(IReadOnlyDictionary<string, FingerprintEntry> entries, Action<string>? warn = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var dtos = entries.ToDictionary(
                kv => kv.Key,
                kv => new EntryDto
                {
                    version = CacheVersion,
                    sizes = kv.Value.FileSizes.ToList(),
                    mtimes = kv.Value.FileMtimes.ToList(),
                    hashes = kv.Value.Fingerprint.Hashes.ToList(),
                    ts = kv.Value.Timestamp,
                });
            File.WriteAllText(CachePath, new SerializerBuilder().Build().Serialize(dtos));
        }
        catch (Exception ex)
        {
            warn?.Invoke($"DiffSinger：写入指纹缓存失败: {ex.Message}");
        }
    }

    public sealed class FingerprintEntry
    {
        public IReadOnlyList<long> FileSizes { get; }
        public IReadOnlyList<long> FileMtimes { get; }
        public ModelFingerprint Fingerprint { get; }
        public long Timestamp { get; }
        public FingerprintEntry(IReadOnlyList<long> fileSizes, IReadOnlyList<long> fileMtimes,
            ModelFingerprint fingerprint, long timestamp)
        {
            FileSizes = fileSizes; FileMtimes = fileMtimes; Fingerprint = fingerprint; Timestamp = timestamp;
        }
    }
}
