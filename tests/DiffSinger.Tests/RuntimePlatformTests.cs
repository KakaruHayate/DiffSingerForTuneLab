using DiffSingerForTuneLab;
using Xunit;

namespace DiffSinger.Tests;

public sealed class RuntimePlatformTests
{
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
}
