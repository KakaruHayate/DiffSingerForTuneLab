using System;
using System.Collections.Generic;
using DiffSingerForTuneLab;
using Xunit;

namespace DiffSinger.Tests;

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
