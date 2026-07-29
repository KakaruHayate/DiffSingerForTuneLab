using System;
using DiffSingerForTuneLab;
using Xunit;

namespace DiffSinger.Tests;

// Absolute variance 的产品语义回归：实数是最终声学目标，NaN 才表示跟随预测。
public class VarianceAbsoluteTests
{
    [Fact]
    public void Voicing_FullRangeTarget_IsAppliedDirectly()
    {
        var spec = VarianceMath.Variances[2];

        float result = VarianceMath.CombineAbsoluteFrame(spec, predicted: -40f, target: 0d);

        Assert.Equal(0f, result);
    }

    [Theory]
    [InlineData(0, -40f)]
    [InlineData(1, -40f)]
    [InlineData(2, -40f)]
    [InlineData(3, 2f)]
    public void NaN_FollowsPrediction(int specIndex, float predicted)
    {
        var spec = VarianceMath.Variances[specIndex];

        float result = VarianceMath.CombineAbsoluteFrame(spec, predicted, double.NaN);

        Assert.Equal(predicted, result);
    }

    [Theory]
    [InlineData(0, -120d, -96f)]
    [InlineData(0, 12d, 0f)]
    [InlineData(1, -120d, -96f)]
    [InlineData(1, 12d, 0f)]
    [InlineData(2, -120d, -96f)]
    [InlineData(2, 12d, 0f)]
    [InlineData(3, -20d, -10f)]
    [InlineData(3, 20d, 10f)]
    public void EditedTarget_IsClampedToAcousticRange(int specIndex, double target, float expected)
    {
        var spec = VarianceMath.Variances[specIndex];

        float result = VarianceMath.CombineAbsoluteFrame(spec, predicted: -40f, target);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, -72d)]
    [InlineData(1, -18.5d)]
    [InlineData(2, -3d)]
    [InlineData(3, 7.25d)]
    public void EditedTarget_DoesNotDependOnPredictionOrDeltaRange(int specIndex, double target)
    {
        var spec = VarianceMath.Variances[specIndex];

        float fromLowPrediction = VarianceMath.CombineAbsoluteFrame(spec, -90f, target);
        float fromHighPrediction = VarianceMath.CombineAbsoluteFrame(spec, 10f, target);

        Assert.Equal((float)target, fromLowPrediction);
        Assert.Equal((float)target, fromHighPrediction);
    }

    [Theory]
    [InlineData(0, -120f, -96f)]
    [InlineData(2, 12f, 0f)]
    [InlineData(3, 20f, 10f)]
    public void NaNPrediction_IsClampedToAcousticRange(int specIndex, float predicted, float expected)
    {
        var spec = VarianceMath.Variances[specIndex];

        float result = VarianceMath.CombineAbsoluteFrame(spec, predicted, double.NaN);

        Assert.Equal(expected, result);
    }
}
