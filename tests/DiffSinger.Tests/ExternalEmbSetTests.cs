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
