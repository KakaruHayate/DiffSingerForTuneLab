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

    // 越界目标：逆函数解出的 y 超编辑量程 → 钳到量程 → 再走 Delta + 声学量程 clamp（与
    //   CombineVarianceAbsolute 的实际管线一致）。结果应落在声学量程内，且等于该方向上可达的极值。
    //   predicted 按各 spec 自己的声学量程取（tension 是 [-10,10]，与 dB 系三者不同刻度）。
    [Theory]
    [InlineData(0.75f, 1.5f)]    // 目标高于上行可达值 → y 钳到 EditMax
    [InlineData(0.25f, -1.5f)]   // 目标低于下行可达值 → y 钳到 EditMin
    public void LinearParams_OutOfRange_Clamped(float predictedPos, float targetPos)
    {
        foreach (var spec in new[] { VarianceMath.Variances[0], VarianceMath.Variances[1], VarianceMath.Variances[3] })
        {
            // 位置 → 该 spec 声学量程内的具体值；target 故意取到量程外以触发钳制。
            float span = (float)(spec.AcousticMax - spec.AcousticMin);
            float predicted = (float)spec.AcousticMin + span * predictedPos;
            float target = (float)spec.AcousticMin + span * targetPos;

            float y = spec.DeltaInverse!(predicted, target);
            float yClamped = (float)Math.Clamp(y, spec.EditMin, spec.EditMax);
            float result = (float)Math.Clamp(spec.Delta(predicted, yClamped), spec.AcousticMin, spec.AcousticMax);

            Assert.InRange(result, spec.AcousticMin, spec.AcousticMax);
            // 钳制方向正确：目标偏高 → 取可达上界；偏低 → 取可达下界。
            float reachable = (float)Math.Clamp(
                spec.Delta(predicted, target > predicted ? (float)spec.EditMax : (float)spec.EditMin),
                spec.AcousticMin, spec.AcousticMax);
            Assert.Equal(reachable, result, precision: 4);
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
    [InlineData(-40f, -32f)]   // 上行分支（y=1+8/48，在编辑量程内）
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
        var spec = VarianceMath.Variances[2];
        // 上行 y > 1 是线性：y = 1 + (target - x) / 48，但结果按编辑量程上限钳到 1.25。
        // 量程内：target = x + 6 → y = 1.125
        Assert.Equal(1.125f, spec.DeltaInverse!(-40f, -34f), precision: 4);
        // 量程外：target = x + 24 → 解析解 1.5，超 EditMax → 钳到 1.25（即 x+12 dB 以上不可达）
        Assert.Equal(1.25f, spec.DeltaInverse!(-40f, -16f), precision: 4);
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
        var positions = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };
        foreach (var spec in VarianceMath.Variances)
        {
            if (spec.DeltaInverse == null) continue;
            foreach (float position in positions)
            {
                float sourceY = (float)(spec.EditMin + (spec.EditMax - spec.EditMin) * position);
                float target = (float)Math.Clamp(
                    spec.Delta(predicted, sourceY), spec.AcousticMin, spec.AcousticMax);
                float recoveredY = Math.Clamp(
                    spec.DeltaInverse(predicted, target), (float)spec.EditMin, (float)spec.EditMax);
                float result = (float)Math.Clamp(
                    spec.Delta(predicted, recoveredY), spec.AcousticMin, spec.AcousticMax);
                Assert.Equal(target, result, precision: 1);  // ±0.1 dB
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

    // 回归：二分比较方向。下行分支 Delta(x, ·) 在 y∈[0,1] 上**单调递增**
    //   （y=0 → -96 dB 静音底，y=1 → 预测值），故二分须在 f(mid) < target 时抬下界。
    //   方向写反会让每次求逆都收敛到区间另一端 —— 输出与用户所画的曲线上下颠倒。
    [Theory]
    [InlineData(-20f)]
    [InlineData(-40f)]
    [InlineData(-60f)]
    public void Voicing_DownwardBranch_IsMonotonicIncreasing(float predicted)
    {
        var spec = VarianceMath.Variances[2];
        // 端点锚定：y=0 触静音底、y=1 回到预测值。
        Assert.Equal(-96f, spec.Delta(predicted, 0f), precision: 3);
        Assert.Equal(predicted, spec.Delta(predicted, 1f), precision: 3);

        // 单调递增：y 增 ⇒ 输出不减。
        float prev = spec.Delta(predicted, 0f);
        for (int i = 1; i <= 200; i++)
        {
            float v = spec.Delta(predicted, i / 200f);
            Assert.True(v >= prev - 1e-3f, $"非单调：y={i / 200f} 处 {v} < 前值 {prev}");
            prev = v;
        }

        // 逆函数随目标单调：目标越高 ⇒ 解出的 y 越大（方向写反时此断言必挂）。
        float yLow = spec.DeltaInverse!(predicted, -90f);
        float yHigh = spec.DeltaInverse!(predicted, predicted - 3f);
        Assert.True(yLow < yHigh, $"逆函数方向反了：y(-90dB)={yLow} 应小于 y({predicted - 3}dB)={yHigh}");
    }
}
