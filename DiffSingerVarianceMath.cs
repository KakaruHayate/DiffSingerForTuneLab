using System;

namespace DiffSingerForTuneLab;

// Variance 参数的纯数学定义（零外部依赖）：delta 公式、声学量程和 absolute 逐帧组合。
//   供单测直接引用（无需 TuneLab SDK）；引擎代码 via DiffSingerDeclarations 引用同一份定义。

// 一个 variance 参数的规格：Delta(x=预测声学值, y=用户归一化值) → 绝对声学值。
public readonly record struct VarianceSpec(
    string Key, string Display, string Color,
    double EditMin, double EditMax, double Neutral,
    double AcousticMin, double AcousticMax,
    Func<float, float, float> Delta);

// 四个 variance 参数的数学定义（与 DiffSingerDeclarations.Variances 同源，此处不含 SDK 依赖）。
public static class VarianceMath
{
    public static readonly VarianceSpec[] Variances =
    {
        new("energy",      "Energy",      "#E573A5", -1, 1, 0, -96, 0,
            (x, y) => x + y * 12),
        new("breathiness", "Breathiness", "#73E5C2", -1, 1, 0, -96, 0,
            (x, y) => x + y * 12),
        new("voicing",     "Voicing",     "#C2E573", 0, 1.25, 1, -96, 0,
            (x, y) => y > 1 ? x + 48 * (y - 1)
                            : x - 48 * (1 - y) / (2 - y) - (x + 72) * MathF.Pow(1 - y, 12)),
        new("tension",     "Tension",     "#A573E5", -1, 1, 0, -10, 10,
            (x, y) => x + y * 5),
    };

    // Absolute 逐帧语义：NaN 表示未编辑，跟随预测；实数表示最终声学目标。
    // 两种路径都钳到参数声学量程，且不受 delta 编辑量程限制。
    public static float CombineAbsoluteFrame(VarianceSpec spec, float predicted, double target)
        => (float)Math.Clamp(double.IsNaN(target) ? predicted : target,
            spec.AcousticMin, spec.AcousticMax);
}
