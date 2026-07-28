using System;
using System.Runtime.InteropServices;

namespace DiffSingerForTuneLab;

internal static class RuntimePlatform
{
    public const string CpuProvider = "cpu";
    public const string DirectMlProvider = "directml";

    public static string RuntimeIdentifier
        => GetRuntimeIdentifier(CurrentOperatingSystem, RuntimeInformation.ProcessArchitecture);

    public static bool SupportsDirectML => RuntimeIdentifier == "win-x64";

    public static string DefaultProvider => SupportsDirectML ? DirectMlProvider : CpuProvider;

    public static string NormalizeProvider(string? provider)
        => SupportsDirectML && string.Equals(provider, DirectMlProvider, StringComparison.OrdinalIgnoreCase)
            ? DirectMlProvider
            : CpuProvider;

    public static bool IsDirectML(string? provider)
        => NormalizeProvider(provider) == DirectMlProvider;

    public static string RuntimeExecutableName => RuntimeIdentifier switch
    {
        "win-x64" => "MLRuntime.exe",
        "osx-arm64" or "linux-x64" => "MLRuntime",
        _ => throw new InvalidOperationException("RuntimeIdentifier returned an unsupported RID."),
    };

    public static string OnnxRuntimeLibraryName => RuntimeIdentifier switch
    {
        "win-x64" => "onnxruntime.dll",
        "osx-arm64" => "libonnxruntime.dylib",
        "linux-x64" => "libonnxruntime.so",
        _ => throw new InvalidOperationException("RuntimeIdentifier returned an unsupported RID."),
    };

    internal static string GetRuntimeIdentifier(OSPlatform operatingSystem, Architecture architecture)
    {
        if (operatingSystem == OSPlatform.Windows && architecture == Architecture.X64)
            return "win-x64";
        if (operatingSystem == OSPlatform.OSX && architecture == Architecture.Arm64)
            return "osx-arm64";
        if (operatingSystem == OSPlatform.Linux && architecture == Architecture.X64)
            return "linux-x64";

        throw new PlatformNotSupportedException(
            $"DiffSinger does not support {operatingSystem} on {architecture}. " +
            "Supported platforms: win-x64, osx-arm64, linux-x64.");
    }

    private static OSPlatform CurrentOperatingSystem
        => OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsLinux() ? OSPlatform.Linux
            : throw new PlatformNotSupportedException(
                "DiffSinger supports only Windows, macOS, and Linux.");
}
