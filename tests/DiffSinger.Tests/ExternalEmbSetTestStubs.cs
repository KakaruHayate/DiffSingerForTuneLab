namespace DiffSingerForTuneLab;

public sealed record ExternalVoice(
    string VoiceId, string Display, string? Color,
    string RootPath, string SpeakerEntry, int Version);

public static class DiffSingerDeclarations
{
    public static string Suffix(string speaker)
    {
        int dot = speaker.LastIndexOf('.');
        return dot >= 0 && dot < speaker.Length - 1 ? speaker[(dot + 1)..] : speaker;
    }
}

public sealed class VoicebankConfig
{
    public string AcousticFileName { get; init; } = string.Empty;
}

public static class DiffSingerTensorCache
{
    public static string CacheDirectory => Path.GetTempPath();
}
