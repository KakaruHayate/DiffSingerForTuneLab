using System;
using System.Runtime.InteropServices;

namespace DiffSingerForTuneLab;

internal static class RuntimePlatform
{
    public const string CpuProvider = "cpu";
    public const string DirectMlProvider = "directml";

    public static bool SupportsDirectML => OperatingSystem.IsWindows();

    public static string DefaultProvider => SupportsDirectML ? DirectMlProvider : CpuProvider;

    public static string NormalizeProvider(string? provider)
        => SupportsDirectML && string.Equals(provider, DirectMlProvider, StringComparison.OrdinalIgnoreCase)
            ? DirectMlProvider
            : CpuProvider;

    public static bool IsDirectML(string? provider)
        => NormalizeProvider(provider) == DirectMlProvider;

    public static string RuntimeExecutableName => OperatingSystem.IsWindows() ? "MLRuntime.exe" : "MLRuntime";

    public static string RuntimeIdentifier
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win"
                : OperatingSystem.IsMacOS() ? "osx"
                : OperatingSystem.IsLinux() ? "linux"
                : throw new PlatformNotSupportedException("DiffSinger 仅支持 Windows、macOS 和 Linux。");
            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "x86",
                Architecture.X64 => "x64",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(
                    $"不支持的进程架构：{RuntimeInformation.ProcessArchitecture}"),
            };
            return $"{os}-{architecture}";
        }
    }

    public static string OnnxRuntimeLibraryName => OperatingSystem.IsWindows() ? "onnxruntime.dll"
        : OperatingSystem.IsMacOS() ? "libonnxruntime.dylib"
        : OperatingSystem.IsLinux() ? "libonnxruntime.so"
        : throw new PlatformNotSupportedException("DiffSinger 仅支持 Windows、macOS 和 Linux。");
}
