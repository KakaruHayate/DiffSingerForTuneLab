using System;
using System.Collections.Generic;
using System.Linq;
using DiffSingerForTuneLab;
using Xunit;

namespace DiffSinger.Tests;

// VarianceSpec DeltaInverse roundtrip 验证：
//   对每个参数，验证 Delta(x, Inverse(x, t)) ≈ t（在声学量程内）。
//   voicing 重点测边界（静音底、满偏、中性点附近）。
//
//   数学定义来自 VarianceMath（零 SDK 依赖），与 DiffSingerDeclarations.Variances 同源。
public class VarianceInverseTests
{
    // ── Energy / Breathiness / Tension：线性解析逆 ──

    [Theory]
    [InlineData( 0f,  -6f)]    // in range: y=-0.5 → exact -6
    [InlineData( 0f,   6f)]    // in range: y=0.5 → exact 6
    [InlineData(-30f, -18f)]   // in range: y=-1 → exact -18
    [InlineData(-48f, -48f)]   // neutral: y=0 → -48
    public void LinearParams_DeltaInverse_ExactForInRange(float predicted, float target)
    {
        foreach (var spec in new[] { VarianceMath.Variances[0], VarianceMath.Variances[1], VarianceMath.Variances[3] })
        {
            float y = spec.DeltaInverse!(predicted, target);
            float result = spec.Delta(predicted, y);
            Assert.Equal(target, result, precision: 5);
        }
    }

    [Theory]
    [InlineData(-60f, -30f)]   // y=2.5 → clamp to 1 → result=-48
    [InlineData(-30f,   0f)]   // y=2.5 → clamp to 1 → result=-18
    [InlineData(-60f, -90f)]   // y=2.5 → clamp to 1 → result=-72
    public void LinearParams_OutOfRange_Clamped(float predicted, float target)
    {
        foreach (var spec in new[] { VarianceMath.Variances[0], VarianceMath.Variances[1], VarianceMath.Variances[3] })
        {
            float y = spec.DeltaInverse!(predicted, target);
            float result = spec.Delta(predicted, (float)Math.Clamp(y, spec.EditMin, spec.EditMax));
            Assert.True(result >= spec.AcousticMin && result <= spec.AcousticMax);
        }
    }

    // ── Voicing 逆函数：边界 + 中值 + roundtrip ──

    [Fact]
    public void Voicing_Invert_Neutral_ReturnsOne()
    {
        // 目标 = 预测值 → y 应 ≈ 1（中性）
        float y = VarianceMath.Variances[2].DeltaInverse!(-40f, -40f);
        Assert.Equal(1f, y, precision: 4);
    }

    [Fact]
    public void Voicing_Invert_SilenceFloor_ReturnsZero()
    {
        // target = -96（静音底），预测任意 → y ≈ 0
        float y = VarianceMath.Variances[2].DeltaInverse!(-40f, -96f);
        Assert.Equal(0f, y, precision: 3);
    }

    [Fact]
    public void Voicing_Invert_FullPositive_ReturnsMax()
    {
        // target = 0（满偏），预测 -40 → y = 1 + 40/48 ≈ 1.833 → clamp 到 1.25
        float y = VarianceMath.Variances[2].DeltaInverse!(-40f, 0f);
        Assert.Equal(1.25f, y, precision: 3);
    }

    [Theory]
    [InlineData(-40f, -20f)]   // 上行分支（target > predicted）
    [InlineData(-40f, -60f)]   // 下行中间
    [InlineData(-80f, -72f)]   // 接近静音底
    [InlineData( 10f,   0f)]   // 预测已在 0dB 以上
    public void Voicing_Roundtrip_MatchesTarget(float predicted, float target)
    {
        var spec = VarianceMath.Variances[2];  // voicing
        float y = spec.DeltaInverse!(predicted, target);
        float result = spec.Delta(predicted, y);
        Assert.Equal(target, result, precision: 2);  // ±0.01 dB 精度足够
    }

    [Fact]
    public void Voicing_InvertUpwardBranch_LinearFormula()
    {
        // 上行 y > 1 是线性：y = 1 + (target - x) / 48
        // target = x + 24 → y = 1.5
        float y = VarianceMath.Variances[2].DeltaInverse!(-40f, -16f);  // -40 + 24 = -16
        Assert.Equal(1.5f, y, precision: 4);
    }

    // ── 全参数遍历 roundtrip（采样密度） ──

    [Theory]
    [InlineData(-80f)]
    [InlineData(-60f)]
    [InlineData(-40f)]
    [InlineData(-20f)]
    [InlineData(  0f)]
    public void AllParams_Roundtrip_SampledTargets(float predicted)
    {
        var targets = new[] { -96f, -72f, -48f, -24f, -12f, 0f };
        foreach (var spec in VarianceMath.Variances)
        {
            if (spec.DeltaInverse == null) continue;
            foreach (var t in targets)
            {
                float y = spec.DeltaInverse(predicted, t);
                float result = spec.Delta(predicted, y);
                Assert.Equal(t, result, precision: 1);  // ±0.1 dB
            }
        }
    }

    // ── Voicing 二分收敛性 ──

    [Fact]
    public void Voicing_Invert_BisectionConverges()
    {
        // 对已知解析解的中点验证迭代精度
        // target = -40, predicted = -40 → y = 1（精确）
        float y = VarianceMath.Variances[2].DeltaInverse!(-40f, -40f);
        Assert.Equal(1f, y, precision: 6);

        // target = -96, predicted = -40 → y = 0（精确触底）
        y = VarianceMath.Variances[2].DeltaInverse!(-40f, -96f);
        Assert.Equal(0f, y, precision: 5);
    }
}
