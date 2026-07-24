using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiffSingerForTuneLab;
using Xunit;

// Tests for zero-SDK types: ModelFingerprintStub + DiffSingerSpeakerMix.
// ModelFingerprintStub replicates ModelFingerprint's value semantics (==, Equals, GetHashCode, ToString)
// without requiring TuneLab SDK. ModelFingerprint.Compute / FingerprintCache require SDK
// (VoicebankConfig / ILogger) — those are exercised by integration; here we test the pure value-type semantics.
namespace DiffSingerForTuneLab;

public class ModelFingerprintStubTests
{
    // —— 相等性 ——

    [Fact]
    public void EmptyFingerprints_AreEqual()
    {
        var a = new ModelFingerprintStub([]);
        var b = new ModelFingerprintStub([]);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void SameHashes_AreEqual()
    {
        var h = new List<ulong> { 1, 2, 3, 4 };
        var a = new ModelFingerprintStub(h);
        var b = new ModelFingerprintStub(h.ToList());
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void DifferentCount_AreNotEqual()
    {
        var a = new ModelFingerprintStub(new ulong[] { 1, 2 });
        var b = new ModelFingerprintStub(new ulong[] { 1, 2, 3 });
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void SameCountDifferentValue_AreNotEqual()
    {
        var a = new ModelFingerprintStub(new ulong[] { 1, 2, 3 });
        var b = new ModelFingerprintStub(new ulong[] { 1, 2, 4 });
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void SingleHash_Equality()
    {
        var a = new ModelFingerprintStub(new ulong[] { 42 });
        var b = new ModelFingerprintStub(new ulong[] { 42 });
        var c = new ModelFingerprintStub(new ulong[] { 43 });
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void LargeHashSet_Equality()
    {
        var h = Enumerable.Range(0, 100).Select(i => (ulong)i * 0xDEADBEEF).ToArray();
        var a = new ModelFingerprintStub(h);
        var b = new ModelFingerprintStub(h.ToList());
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    // —— GetHashCode 一致性 ——

    [Fact]
    public void EqualFingerprints_HaveSameHashCode()
    {
        var h = new List<ulong> { 10, 20, 30 };
        var a = new ModelFingerprintStub(h);
        var b = new ModelFingerprintStub(h.ToList());
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentFingerprints_UsuallyHaveDifferentHashCode()
    {
        var a = new ModelFingerprintStub(new ulong[] { 1 });
        var b = new ModelFingerprintStub(new ulong[] { 2 });
        // Not a guarantee, but overwhelmingly likely for ulong values.
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void HashCode_StableAcrossCalls()
    {
        var fp = new ModelFingerprintStub(new ulong[] { 5, 10, 15 });
        var h1 = fp.GetHashCode();
        var h2 = fp.GetHashCode();
        Assert.Equal(h1, h2);
    }

    // —— ToString ——

    [Fact]
    public void ToString_IncludesCount()
    {
        var fp = new ModelFingerprintStub(new ulong[] { 1, 2, 3 });
        Assert.Equal("Fingerprint(3 hashes)", fp.ToString());
    }

    [Fact]
    public void ToString_EmptyFingerprint()
    {
        var fp = new ModelFingerprintStub([]);
        Assert.Equal("Fingerprint(0 hashes)", fp.ToString());
    }

    // —— Equals(object) ——

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var fp = new ModelFingerprintStub(new ulong[] { 1 });
        Assert.False(fp.Equals(null));
    }

    [Fact]
    public void Equals_OtherType_ReturnsFalse()
    {
        var fp = new ModelFingerprintStub(new ulong[] { 1 });
        Assert.False(fp.Equals(42));
        Assert.False(fp.Equals("string"));
    }

    // —— 用作 Dictionary 键 ——

    [Fact]
    public void CanBeUsedAsDictionaryKey()
    {
        var dict = new Dictionary<ModelFingerprintStub, string>();
        var fp1 = new ModelFingerprintStub(new ulong[] { 100, 200 });
        var fp2 = new ModelFingerprintStub(new ulong[] { 100, 200 });
        var fp3 = new ModelFingerprintStub(new ulong[] { 300 });

        dict[fp1] = "a";
        dict[fp2] = "b";   // equal key → overwrites
        dict[fp3] = "c";

        Assert.Equal(2, dict.Count);
        Assert.Equal("b", dict[fp1]);   // fp1 == fp2 → same entry
        Assert.Equal("c", dict[fp3]);
    }

    [Fact]
    public void Dictionary_ContainsKey_WorksWithEqualValue()
    {
        var fp1 = new ModelFingerprintStub(new ulong[] { 7, 8 });
        var fp2 = new ModelFingerprintStub(new ulong[] { 7, 8 });
        var dict = new Dictionary<ModelFingerprintStub, string> { [fp1] = "x" };
        Assert.True(dict.ContainsKey(fp2));
    }

    // —— 三元一致性（CodeRabbit review 关注点）——

    [Fact]
    public void TripleConsistency_EqualValues()
    {
        var h = new List<ulong> { 99 };
        var a = new ModelFingerprintStub(h);
        var b = new ModelFingerprintStub(h.ToList());
        // a == b ⟺ a.Equals(b) ⟺ a.GetHashCode() == b.GetHashCode()
        Assert.True(a == b);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TripleConsistency_NotEqual()
    {
        var a = new ModelFingerprintStub(new ulong[] { 1, 2 });
        var b = new ModelFingerprintStub(new ulong[] { 1, 3 });
        Assert.False(a == b);
        Assert.False(a.Equals(b));
        // GetHashCode may or may not differ — not part of the consistency contract for unequal values.
    }
}

public class DiffSingerSpeakerMixTests
{
    // Direct tests for DiffSingerSpeakerMix (zero SDK dependency).
    // These complement the existing DiffSingerFramesTests.

    [Fact]
    public void Create_SingleTrack_NoMix_NormalizesToOne()
    {
        // 无 mix 轨（tracks 空）→ 默认 suffix 权重全 1。
        var mix = DiffSingerSpeakerMix.Create("Miku", [], 5);
        Assert.Equal(5, mix.FrameCount);
        var emb = mix.ToEmbedding(key => key == "Miku" ? new float[] { 1f, 2f, 3f } : new float[] { 0f, 0f, 0f }, 3);
        // 每帧 Miku 权重 1.0: 帧0=[1,2,3], 帧1=[1,2,3], ...
        for (int f = 0; f < 5; f++)
        {
            Assert.Equal(1f, emb[f * 3 + 0]);
            Assert.Equal(2f, emb[f * 3 + 1]);
            Assert.Equal(3f, emb[f * 3 + 2]);
        }
    }

    [Fact]
    public void Create_MixTrack_SumToOne_NoNormalization()
    {
        // 默认 0.5 + 外部 0.5 = 1.0，无需归一化。
        var tracks = new List<(string, double[])> { ("Miku", [0.5]), ("Luka", [0.5]) };
        var mix = DiffSingerSpeakerMix.Create("Miku", tracks, 1);
        var emb = mix.ToEmbedding(key =>
            key == "Miku" ? new float[] { 1f } : key == "Luka" ? new float[] { 3f } : new float[] { 0f }, 1);
        // Miku=0.5, Luka=0.5 → 0.5*1 + 0.5*3 = 2.0
        Assert.Equal(2.0f, emb[0], precision: 4);
    }

    [Fact]
    public void Create_MixTrack_SumOverOne_Normalizes()
    {
        // 默认 0.8 + 外部 0.8 = 1.6 > 1 → 各除 1.6。
        var tracks = new List<(string, double[])> { ("Miku", [0.8]), ("Luka", [0.8]) };
        var mix = DiffSingerSpeakerMix.Create("Miku", tracks, 1);
        var emb = mix.ToEmbedding(key =>
            key == "Miku" ? new float[] { 1f } : key == "Luka" ? new float[] { 3f } : new float[] { 0f }, 1);
        // 归一化后 Miku=0.5, Luka=0.5 → 0.5*1 + 0.5*3 = 2.0
        Assert.Equal(2.0f, emb[0], precision: 4);
    }

    [Fact]
    public void Create_MixTrack_SumUnderOne_FillsDefault()
    {
        // 默认 0.3 + 外部 0.3 = 0.6 < 1 → 默认补 0.4。
        var tracks = new List<(string, double[])> { ("Miku", [0.3]), ("Luka", [0.3]) };
        var mix = DiffSingerSpeakerMix.Create("Miku", tracks, 1);
        var emb = mix.ToEmbedding(key =>
            key == "Miku" ? new float[] { 1f } : key == "Luka" ? new float[] { 3f } : new float[] { 0f }, 1);
        // Miku=0.7, Luka=0.3 → 0.7*1 + 0.3*3 = 1.6
        Assert.Equal(1.6f, emb[0], precision: 4);
    }

    [Fact]
    public void Create_MultiFrame_TrackShorterThanFrames()
    {
        // tracks 数组短于 nFrames → 缺失帧视作 NaN（权重 0）。
        var tracks = new List<(string, double[])> { ("Luka", [0.5]) };
        var mix = DiffSingerSpeakerMix.Create("Miku", tracks, 3);
        var emb = mix.ToEmbedding(key =>
            key == "Miku" ? new float[] { 10f, 20f, 30f } : new float[] { 1f, 2f, 3f }, 3);
        // Weights: Miku=[0.5,1,1], Luka=[0.5,0,0]
        // 帧0: 0.5*[10,20,30] + 0.5*[1,2,3] = [5.5, 11, 16.5]
        // 帧1: 1.0*[10,20,30] = [10, 20, 30]
        // 帧2: 1.0*[10,20,30] = [10, 20, 30]
        Assert.Equal(3, mix.FrameCount);
        Assert.Equal(5.5f, emb[0], precision: 4);
        Assert.Equal(11f, emb[1], precision: 4);
        Assert.Equal(16.5f, emb[2], precision: 4);
        Assert.Equal(10f, emb[3], precision: 4);
        Assert.Equal(20f, emb[4], precision: 4);
        Assert.Equal(30f, emb[5], precision: 4);
        Assert.Equal(10f, emb[6], precision: 4);
        Assert.Equal(20f, emb[7], precision: 4);
        Assert.Equal(30f, emb[8], precision: 4);
    }

    [Fact]
    public void ToEmbedding_ZeroWeight_SkipsContribution()
    {
        // 某 suffix 权重恒 0 → 不贡献（ToEmbedding 的 w==0 continue）。
        // 注意：resolveEmb 对 mEntries 中每个 suffix 都会调用一次（含零权重），
        // 但 zero-weight 的结果不参与累加。
        var tracks = new List<(string, double[])> { ("Luka", [0.0]) };
        var mix = DiffSingerSpeakerMix.Create("Miku", tracks, 1);
        // Luka 权重 0 → 即使 resolver 返回非零，不影响结果。
        var emb = mix.ToEmbedding(key =>
            key == "Miku" ? new float[] { 1f } : key == "Luka" ? new float[] { 99f } : new float[] { 0f }, 1);
        Assert.Equal(new float[] { 1f }, emb);
    }

    [Fact]
    public void Create_FiveFrames_SingleTrack()
    {
        var tracks = new List<(string, double[])> { ("Luka", [0.3, 0.6, 0.9, 0.4, 0.0]) };
        var mix = DiffSingerSpeakerMix.Create("Miku", tracks, 5);
        Assert.Equal(5, mix.FrameCount);
    }
}
