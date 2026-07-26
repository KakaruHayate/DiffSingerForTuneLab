using DiffSingerForTuneLab;
using Xunit;

namespace DiffSinger.Tests;

public sealed class ExternalEmbSetTests : IDisposable
{
    readonly string mRoot = Path.Combine(Path.GetTempPath(), $"DiffSingerExternalEmbSet-{Guid.NewGuid():N}");

    [Fact]
    public void NativeVoiceId_ReturnsFalseInsteadOfZeroVector()
    {
        Directory.CreateDirectory(mRoot);
        var set = CreateSet(acousticHidden: 2, pitchHidden: 3, varianceHidden: 4);

        Assert.False(set.TryAcoustic("native", out var acoustic));
        Assert.False(set.TryPitch("native", out var pitch));
        Assert.False(set.TryVariance("native", out var variance));
        Assert.Empty(acoustic);
        Assert.Empty(pitch);
        Assert.Empty(variance);
    }

    [Fact]
    public void Domains_ReadUsingTheirOwnHiddenSizes()
    {
        Directory.CreateDirectory(Path.Combine(mRoot, "dspitch"));
        Directory.CreateDirectory(Path.Combine(mRoot, "dsvariance"));
        WriteEmb(Path.Combine(mRoot, "speaker.emb"), 1f, 2f);
        WriteEmb(Path.Combine(mRoot, "dspitch", "speaker.emb"), 3f, 4f, 5f);
        WriteEmb(Path.Combine(mRoot, "dsvariance", "speaker.emb"), 6f, 7f, 8f, 9f);
        var set = CreateSet(acousticHidden: 2, pitchHidden: 3, varianceHidden: 4);

        Assert.True(set.TryAcoustic("external", out var acoustic));
        Assert.True(set.TryPitch("external", out var pitch));
        Assert.True(set.TryVariance("external", out var variance));
        Assert.Equal(new[] { 1f, 2f }, acoustic);
        Assert.Equal(new[] { 3f, 4f, 5f }, pitch);
        Assert.Equal(new[] { 6f, 7f, 8f, 9f }, variance);
    }

    [Fact]
    public void MissingExternalEmbedding_ReturnsDomainSizedZeroVector()
    {
        Directory.CreateDirectory(mRoot);
        var set = CreateSet(acousticHidden: 2, pitchHidden: 3, varianceHidden: 4);

        Assert.True(set.TryPitch("external", out var pitch));
        Assert.Equal(3, pitch.Length);
        Assert.All(pitch, value => Assert.Equal(0f, value));
    }

    [Fact]
    public void NativeKeyCollision_IsExcludedFromExternalResolution()
    {
        Directory.CreateDirectory(mRoot);
        var set = new ExternalEmbSet(2, 3, 4,
            [new ExternalVoice("shared", "External", null, mRoot, "speaker", 1)],
            new HashSet<string>(StringComparer.Ordinal) { "shared" });

        Assert.False(set.TryAcoustic("shared", out var acoustic));
        Assert.Empty(acoustic);
    }

    // 回归：manifest 包的 SpeakerEntry 是 dsconfig **后缀**，而 .emb 文件名是「前缀.后缀」的完整 entry。
    //   直取必然落空，须经该域 speakers 表反查。此前 acoustic 域漏了这步反查（只 predictor 域有），
    //   导致 TryAcoustic 返回 true + 全零向量——零向量照常按权重进归一化分母，听感是目标音色被稀释、无任何报错。
    [Fact]
    public void PrefixedEntry_AcousticResolvesViaSpeakersTable()
    {
        Directory.CreateDirectory(mRoot);
        File.WriteAllText(Path.Combine(mRoot, "dsconfig.yaml"),
            "speakers:\n  - pack.singer\n  - pack.other\n");
        WriteEmb(Path.Combine(mRoot, "pack.singer.emb"), 1f, 2f);
        // SpeakerEntry 传后缀（manifest voices[].speaker 语义），而非文件名。
        var set = new ExternalEmbSet(2, 3, 4,
            [new ExternalVoice("external", "External", null, mRoot, "singer", 1)]);

        Assert.True(set.TryAcoustic("external", out var acoustic));
        Assert.Equal(new[] { 1f, 2f }, acoustic);
    }

    [Fact]
    public void PrefixedEntry_PredictorDomainsResolveViaSpeakersTable()
    {
        Directory.CreateDirectory(Path.Combine(mRoot, "dspitch"));
        Directory.CreateDirectory(Path.Combine(mRoot, "dsvariance"));
        File.WriteAllText(Path.Combine(mRoot, "dspitch", "dsconfig.yaml"), "speakers:\n  - pitchpack.singer\n");
        File.WriteAllText(Path.Combine(mRoot, "dsvariance", "dsconfig.yaml"), "speakers:\n  - varpack.singer\n");
        WriteEmb(Path.Combine(mRoot, "dspitch", "pitchpack.singer.emb"), 3f, 4f, 5f);
        WriteEmb(Path.Combine(mRoot, "dsvariance", "varpack.singer.emb"), 6f, 7f, 8f, 9f);
        var set = new ExternalEmbSet(2, 3, 4,
            [new ExternalVoice("external", "External", null, mRoot, "singer", 1)]);

        Assert.True(set.TryPitch("external", out var pitch));
        Assert.True(set.TryVariance("external", out var variance));
        Assert.Equal(new[] { 3f, 4f, 5f }, pitch);
        Assert.Equal(new[] { 6f, 7f, 8f, 9f }, variance);
    }

    // 取不到 emb 时必须告警：零向量会稀释音色而非静音，静默失败无从排查。
    [Fact]
    public void MissingEmbedding_InvokesWarnCallback()
    {
        Directory.CreateDirectory(mRoot);
        var warnings = new List<string>();
        var set = new ExternalEmbSet(2, 3, 4,
            [new ExternalVoice("external", "External", null, mRoot, "speaker", 1)],
            excludedVoiceIds: null, warn: warnings.Add);

        Assert.True(set.TryAcoustic("external", out var acoustic));
        Assert.All(acoustic, value => Assert.Equal(0f, value));
        Assert.Single(warnings);
        Assert.Contains("acoustic", warnings[0]);
    }

    // 直取仍须优先：legacy 包的 SpeakerEntry 本就是完整 entry 名，不该被表反查绕开。
    [Fact]
    public void FullEntryName_StillResolvesDirectly()
    {
        Directory.CreateDirectory(mRoot);
        WriteEmb(Path.Combine(mRoot, "speaker.emb"), 1f, 2f);
        var set = CreateSet(acousticHidden: 2, pitchHidden: 3, varianceHidden: 4);

        Assert.True(set.TryAcoustic("external", out var acoustic));
        Assert.Equal(new[] { 1f, 2f }, acoustic);
    }

    ExternalEmbSet CreateSet(int acousticHidden, int pitchHidden, int varianceHidden)
        => new(acousticHidden, pitchHidden, varianceHidden,
            [new ExternalVoice("external", "External", null, mRoot, "speaker", 1)]);

    static void WriteEmb(string path, params float[] values)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        foreach (float value in values)
            writer.Write(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(mRoot))
            Directory.Delete(mRoot, recursive: true);
    }
}
