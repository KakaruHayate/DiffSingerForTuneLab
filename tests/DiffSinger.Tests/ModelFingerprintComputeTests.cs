using DiffSingerForTuneLab;
using Xunit;

namespace DiffSinger.Tests;

public sealed class ModelFingerprintComputeTests : IDisposable
{
    readonly string mRoot = Path.Combine(Path.GetTempPath(), $"DiffSingerFingerprint-{Guid.NewGuid():N}");
    readonly VoicebankConfig mConfig = new() { AcousticFileName = "acoustic.onnx" };

    [Fact]
    public void DifferentPredictorRoles_CannotCollapseToSameHashSequence()
    {
        string durRoot = Path.Combine(mRoot, "dur-only");
        string pitchRoot = Path.Combine(mRoot, "pitch-only");
        WriteConfig(Path.Combine(durRoot, "dsdur"), "dur");
        WriteConfig(Path.Combine(pitchRoot, "dspitch"), "pitch");

        var dur = ModelFingerprint.Compute(durRoot, mConfig, HashFileName);
        var pitch = ModelFingerprint.Compute(pitchRoot, mConfig, HashFileName);

        Assert.Equal(ModelFingerprint.SlotCount, dur.Hashes.Count);
        Assert.Equal(ModelFingerprint.SlotCount, pitch.Hashes.Count);
        Assert.NotEqual(dur, pitch);
    }

    [Fact]
    public void MissingPredictors_OccupyStableSlots()
    {
        Directory.CreateDirectory(mRoot);

        var fingerprint = ModelFingerprint.Compute(mRoot, mConfig, HashFileName);

        Assert.Equal(new ulong[]
        {
            1,
            ModelFingerprint.MissingSlot, ModelFingerprint.MissingSlot,
            ModelFingerprint.MissingSlot, ModelFingerprint.MissingSlot,
            ModelFingerprint.MissingSlot, ModelFingerprint.MissingSlot,
        }, fingerprint.Hashes);
    }

    [Fact]
    public void InvalidPredictorConfig_IsNotTreatedAsMissing()
    {
        string dir = Path.Combine(mRoot, "dsdur");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dsconfig.yaml"), "\tinvalid");

        Assert.Throws<InvalidOperationException>(() =>
            ModelFingerprint.Compute(mRoot, mConfig, HashFileName));
    }

    static ulong HashFileName(string path) => Path.GetFileName(path) switch
    {
        "acoustic.onnx" => 1,
        "ling.onnx" => 2,
        "role.onnx" => 3,
        _ => throw new InvalidOperationException(path),
    };

    static void WriteConfig(string dir, string role)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dsconfig.yaml"),
            $"linguistic: ling.onnx\n{role}: role.onnx\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(mRoot))
            Directory.Delete(mRoot, recursive: true);
    }
}
