using System;
using System.Collections.Generic;

namespace DiffSinger.Tests;

// ModelFingerprint 的纯值语义测试 stub（零 SDK 依赖）。
//   ModelFingerprint.Compute 依赖 VoicebankConfig/ILogger，此处不引入；
//   只测相等性 / GetHashCode / ToString / Equals(object) / Dictionary 键行为。
//   这些测试同时验证 == 运算符、Equals、GetHashCode 三者的一致性（CodeRabbit review 关注点）。
internal readonly struct ModelFingerprintStub : IEquatable<ModelFingerprintStub>
{
    public IReadOnlyList<ulong> Hashes { get; }
    public ModelFingerprintStub(IReadOnlyList<ulong>? hashes) => Hashes = hashes ?? Array.Empty<ulong>();
    public static bool operator ==(ModelFingerprintStub a, ModelFingerprintStub b)
    {
        if (a.Hashes.Count != b.Hashes.Count) return false;
        for (int i = 0; i < a.Hashes.Count; i++)
            if (a.Hashes[i] != b.Hashes[i]) return false;
        return true;
    }
    public static bool operator !=(ModelFingerprintStub a, ModelFingerprintStub b) => !(a == b);
    public override int GetHashCode()
    {
        int h = HashCode.Combine(Hashes.Count);
        foreach (var v in Hashes) h = HashCode.Combine(h, (int)(v ^ (v >> 32)));
        return h;
    }
    public bool Equals(ModelFingerprintStub other) => this == other;
    public override bool Equals(object? obj) => obj is ModelFingerprintStub f && this == f;
    public override string ToString() => $"Fingerprint({Hashes.Count} hashes)";
}
