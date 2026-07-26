using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiffSingerForTuneLab;
using Xunit;

// Exercise the production ModelFingerprint value semantics directly. Test stubs provide only
// the SDK-adjacent dependencies required to compile ModelFingerprint.cs in this test assembly.
namespace DiffSinger.Tests;

public class ModelFingerprintTests
{
    // —— 相等性 ——

    [Fact]
    public void EmptyFingerprints_AreEqual()
    {
        var a = new ModelFingerprint([]);
        var b = new ModelFingerprint([]);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void SameHashes_AreEqual()
    {
        var h = new List<ulong> { 1, 2, 3, 4 };
        var a = new ModelFingerprint(h);
        var b = new ModelFingerprint(h.ToList());
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void DifferentCount_AreNotEqual()
    {
        var a = new ModelFingerprint(new ulong[] { 1, 2 });
        var b = new ModelFingerprint(new ulong[] { 1, 2, 3 });
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void SameCountDifferentValue_AreNotEqual()
    {
        var a = new ModelFingerprint(new ulong[] { 1, 2, 3 });
        var b = new ModelFingerprint(new ulong[] { 1, 2, 4 });
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void SingleHash_Equality()
    {
        var a = new ModelFingerprint(new ulong[] { 42 });
        var b = new ModelFingerprint(new ulong[] { 42 });
        var c = new ModelFingerprint(new ulong[] { 43 });
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void LargeHashSet_Equality()
    {
        var h = Enumerable.Range(0, 100).Select(i => (ulong)i * 0xDEADBEEF).ToArray();
        var a = new ModelFingerprint(h);
        var b = new ModelFingerprint(h.ToList());
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    // —— GetHashCode 一致性 ——

    [Fact]
    public void EqualFingerprints_HaveSameHashCode()
    {
        var h = new List<ulong> { 10, 20, 30 };
        var a = new ModelFingerprint(h);
        var b = new ModelFingerprint(h.ToList());
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentFingerprints_UsuallyHaveDifferentHashCode()
    {
        var a = new ModelFingerprint(new ulong[] { 1 });
        var b = new ModelFingerprint(new ulong[] { 2 });
        // Not a guarantee, but overwhelmingly likely for ulong values.
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void HashCode_StableAcrossCalls()
    {
        var fp = new ModelFingerprint(new ulong[] { 5, 10, 15 });
        var h1 = fp.GetHashCode();
        var h2 = fp.GetHashCode();
        Assert.Equal(h1, h2);
    }

    // —— ToString ——

    [Fact]
    public void ToString_IncludesCount()
    {
        var fp = new ModelFingerprint(new ulong[] { 1, 2, 3 });
        Assert.Equal("Fingerprint(3 hashes)", fp.ToString());
    }

    [Fact]
    public void ToString_EmptyFingerprint()
    {
        var fp = new ModelFingerprint([]);
        Assert.Equal("Fingerprint(0 hashes)", fp.ToString());
    }

    // —— Equals(object) ——

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var fp = new ModelFingerprint(new ulong[] { 1 });
        Assert.False(fp.Equals(null));
    }

    [Fact]
    public void Equals_OtherType_ReturnsFalse()
    {
        var fp = new ModelFingerprint(new ulong[] { 1 });
        Assert.False(fp.Equals(42));
        Assert.False(fp.Equals("string"));
    }

    // —— 用作 Dictionary 键 ——

    [Fact]
    public void CanBeUsedAsDictionaryKey()
    {
        var dict = new Dictionary<ModelFingerprint, string>();
        var fp1 = new ModelFingerprint(new ulong[] { 100, 200 });
        var fp2 = new ModelFingerprint(new ulong[] { 100, 200 });
        var fp3 = new ModelFingerprint(new ulong[] { 300 });

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
        var fp1 = new ModelFingerprint(new ulong[] { 7, 8 });
        var fp2 = new ModelFingerprint(new ulong[] { 7, 8 });
        var dict = new Dictionary<ModelFingerprint, string> { [fp1] = "x" };
        Assert.True(dict.ContainsKey(fp2));
    }

    // —— 三元一致性（CodeRabbit review 关注点）——

    [Fact]
    public void TripleConsistency_EqualValues()
    {
        var h = new List<ulong> { 99 };
        var a = new ModelFingerprint(h);
        var b = new ModelFingerprint(h.ToList());
        // a == b ⟺ a.Equals(b) ⟺ a.GetHashCode() == b.GetHashCode()
        Assert.True(a == b);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TripleConsistency_NotEqual()
    {
        var a = new ModelFingerprint(new ulong[] { 1, 2 });
        var b = new ModelFingerprint(new ulong[] { 1, 3 });
        Assert.False(a == b);
        Assert.False(a.Equals(b));
        // GetHashCode may or may not differ — not part of the consistency contract for unequal values.
    }
}
