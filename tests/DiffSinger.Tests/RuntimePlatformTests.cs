using System;
using System.Runtime.InteropServices;
using DiffSingerForTuneLab;
using Xunit;

namespace DiffSinger.Tests;

public sealed class RuntimePlatformTests
{
    [Theory]
    [InlineData("windows", Architecture.X64, "win-x64")]
    [InlineData("osx", Architecture.Arm64, "osx-arm64")]
    [InlineData("linux", Architecture.X64, "linux-x64")]
    public void GetRuntimeIdentifier_AcceptsPublishedPlatforms(
        string operatingSystem,
        Architecture architecture,
        string expected)
        => Assert.Equal(
            expected,
            RuntimePlatform.GetRuntimeIdentifier(ToPlatform(operatingSystem), architecture));

    [Theory]
    [InlineData("windows", Architecture.X86)]
    [InlineData("windows", Architecture.Arm)]
    [InlineData("windows", Architecture.Arm64)]
    [InlineData("osx", Architecture.X86)]
    [InlineData("osx", Architecture.X64)]
    [InlineData("osx", Architecture.Arm)]
    [InlineData("linux", Architecture.X86)]
    [InlineData("linux", Architecture.Arm)]
    [InlineData("linux", Architecture.Arm64)]
    public void GetRuntimeIdentifier_RejectsUnpublishedPlatforms(
        string operatingSystem,
        Architecture architecture)
    {
        var exception = Assert.Throws<PlatformNotSupportedException>(
            () => RuntimePlatform.GetRuntimeIdentifier(ToPlatform(operatingSystem), architecture));

        Assert.Contains("win-x64, osx-arm64, linux-x64", exception.Message);
    }

    [Fact]
    public void NormalizeProvider_AlwaysAcceptsCpu()
        => Assert.Equal(RuntimePlatform.CpuProvider, RuntimePlatform.NormalizeProvider("cpu"));

    [Fact]
    public void NormalizeProvider_RejectsUnknownProvider()
        => Assert.Equal(RuntimePlatform.CpuProvider, RuntimePlatform.NormalizeProvider("cuda"));

    [Fact]
    public void NormalizeProvider_OnlyAllowsDirectMlOnWindows()
    {
        var expected = OperatingSystem.IsWindows()
            ? RuntimePlatform.DirectMlProvider
            : RuntimePlatform.CpuProvider;
        Assert.Equal(expected, RuntimePlatform.NormalizeProvider("directml"));
    }

    [Fact]
    public void RuntimeNamesMatchCurrentOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("MLRuntime.exe", RuntimePlatform.RuntimeExecutableName);
            Assert.Equal("onnxruntime.dll", RuntimePlatform.OnnxRuntimeLibraryName);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal("MLRuntime", RuntimePlatform.RuntimeExecutableName);
            Assert.Equal("libonnxruntime.dylib", RuntimePlatform.OnnxRuntimeLibraryName);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.Equal("MLRuntime", RuntimePlatform.RuntimeExecutableName);
            Assert.Equal("libonnxruntime.so", RuntimePlatform.OnnxRuntimeLibraryName);
        }
    }

    private static OSPlatform ToPlatform(string operatingSystem)
        => operatingSystem switch
        {
            "windows" => OSPlatform.Windows,
            "osx" => OSPlatform.OSX,
            "linux" => OSPlatform.Linux,
            _ => throw new ArgumentOutOfRangeException(nameof(operatingSystem)),
        };
}
