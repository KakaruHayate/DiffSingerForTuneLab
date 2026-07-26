using System;

namespace DiffSingerForTuneLab;

// Variance 参数的纯数学定义（零外部依赖）：Delta 函数、DeltaInverse 逆函数、VarianceSpec 记录。
//   供单测直接引用（无需 TuneLab SDK）；引擎代码 via DiffSingerDeclarations 引用同一份定义。

// 一个 variance 参数的规格：Delta(x=预测声学值, y=用户归一化值) → 绝对声学值。
//   DeltaInverse（可选）：给定 x 和目标绝对值 t，求等效 delta 系数 y。
public readonly record struct VarianceSpec(
    string Key, string Display, string Color,
    double EditMin, double EditMax, double Neutral,
    double AcousticMin, double AcousticMax,
    Func<float, float, float> Delta,
    Func<float, float, float>? DeltaInverse = null);

// 四个 variance 参数的数学定义（与 DiffSingerDeclarations.Variances 同源，此处不含 SDK 依赖）。
public static class VarianceMath
{
    public static readonly VarianceSpec[] Variances =
    {
        new("energy",      "Energy",      "#E573A5", -1, 1, 0, -96, 0,
            (x, y) => x + y * 12,      (x, t) => (t - x) / 12f),
        new("breathiness", "Breathiness", "#73E5C2", -1, 1, 0, -96, 0,
            (x, y) => x + y * 12,      (x, t) => (t - x) / 12f),
        new("voicing",     "Voicing",     "#C2E573", 0, 1.25, 1, -96, 0,
            (x, y) => y > 1 ? x + 48 * (y - 1)
                            : x - 48 * (1 - y) / (2 - y) - (x + 72) * MathF.Pow(1 - y, 12),
            InvertVoicing),
        new("tension",     "Tension",     "#A573E5", -1, 1, 0, -10, 10,
            (x, y) => x + y * 5,       (x, t) => (t - x) / 5f),
    };

    // Voicing delta 下行逆函数（数值二分求 y ∈ [0,1]）。
    //   下行 Delta(x, ·) 在 [0,1] 上随 y 递增：y=0 → −96 dB（静音底）、y=1 → 预测值 x。
    //   故二分条件是 f(mid) < target 时抬下界；写反会收敛到区间另一端、令输出与用户所画曲线上下颠倒
    //   （见 VarianceInverseTests.Voicing_DownwardBranch_IsMonotonicIncreasing 回归）。
    //   注：x < −72 时公式在 y≈0.05~0.24 附近会下探到 −96 以下（幂项系数 x+72 变号），非严格单调；
    //   但整段下探都在声学量程外，调用方的 [-96,0] clamp 已将其压平，可达目标的求逆精度不受影响。
    public static float InvertVoicing(float x, float target)
    {
        float yUp = 1f + (target - x) / 48f;
        if (yUp > 1f) return Math.Clamp(yUp, 1f, 1.25f);
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 40; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (VoicingDeltaDown(x, mid) < target) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    static float VoicingDeltaDown(float x, float y)
        => x - 48f * (1f - y) / (2f - y) - (x + 72f) * MathF.Pow(1f - y, 12f);
}
