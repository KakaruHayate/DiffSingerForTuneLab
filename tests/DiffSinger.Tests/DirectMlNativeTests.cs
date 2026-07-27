using System;
using System.IO;
using System.Runtime.InteropServices;
using DiffSingerForTuneLab;
using Xunit;

namespace DiffSinger.Tests;

// DirectMlNative.Preload：幂等、尽力而为，文件不在或加载失败都不抛（见类注释）。
//   本 PR 把注释中引用的 onnxruntime 版本由 1.20 更新为 1.23（无逻辑改动），
//   但既然此文件在改动范围内，覆盖其既有的“绝不抛异常”契约与 rid 目录探测逻辑。
public class DirectMlNativeTests : IDisposable
{
    readonly string mTempDir;

    public DirectMlNativeTests()
    {
        mTempDir = Path.Combine(Path.GetTempPath(), "DirectMlNativeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mTempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(mTempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    static string CurrentRid => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => "win-x64",
    };

    [Fact]
    public void Preload_NonExistentDirectory_DoesNotThrow()
    {
        var missing = Path.Combine(mTempDir, "does-not-exist");
        var ex = Record.Exception(() => DirectMlNative.Preload(missing));
        Assert.Null(ex);
    }

    [Fact]
    public void Preload_EmptyRootDirectory_DoesNotThrow()
    {
        // 目录存在但无 runtimes/ 子结构 → File.Exists 应为 false，直接跳过加载。
        var ex = Record.Exception(() => DirectMlNative.Preload(mTempDir));
        Assert.Null(ex);
    }

    [Fact]
    public void Preload_NullDirectory_DoesNotThrow()
    {
        // Path.Combine(null, ...) 内部会抛 ArgumentNullException，方法的 catch-all 应吞掉它。
        var ex = Record.Exception(() => DirectMlNative.Preload(null!));
        Assert.Null(ex);
    }

    [Fact]
    public void Preload_EmptyStringDirectory_DoesNotThrow()
    {
        var ex = Record.Exception(() => DirectMlNative.Preload(string.Empty));
        Assert.Null(ex);
    }

    [Fact]
    public void Preload_WhitespaceDirectory_DoesNotThrow()
    {
        var ex = Record.Exception(() => DirectMlNative.Preload("   "));
        Assert.Null(ex);
    }

    [Fact]
    public void Preload_RelativePathDirectory_DoesNotThrow()
    {
        var ex = Record.Exception(() => DirectMlNative.Preload("."));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("win-x86")]
    [InlineData("win-arm64")]
    [InlineData("win-x64")]
    public void Preload_InvalidDllPresentUnderAnyRid_DoesNotThrow(string rid)
    {
        // 不论测试运行环境实际架构映射到哪个 rid，只要目标路径存在一个（非真实原生库的）文件，
        // Preload 都不应抛出——无效原生库会被 NativeLibrary.TryLoad 静默吞掉（幂等、尽力而为契约）。
        var nativeDir = Path.Combine(mTempDir, "runtimes", rid, "native");
        Directory.CreateDirectory(nativeDir);
        File.WriteAllBytes(Path.Combine(nativeDir, "DirectML.dll"), new byte[] { 0x00, 0x01, 0x02, 0x03 });

        var ex = Record.Exception(() => DirectMlNative.Preload(mTempDir));
        Assert.Null(ex);
    }

    [Fact]
    public void Preload_CalledTwiceWithSamePath_IsIdempotentAndDoesNotThrow()
    {
        var nativeDir = Path.Combine(mTempDir, "runtimes", CurrentRid, "native");
        Directory.CreateDirectory(nativeDir);
        File.WriteAllBytes(Path.Combine(nativeDir, "DirectML.dll"), new byte[] { 0x00, 0x01, 0x02, 0x03 });

        var ex1 = Record.Exception(() => DirectMlNative.Preload(mTempDir));
        var ex2 = Record.Exception(() => DirectMlNative.Preload(mTempDir));
        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    [Fact]
    public void Preload_MatchingRidDirectoryButWrongFileName_DoesNotThrow()
    {
        // 目录结构正确，但文件名不是 DirectML.dll → File.Exists 应为 false，直接跳过加载。
        var nativeDir = Path.Combine(mTempDir, "runtimes", CurrentRid, "native");
        Directory.CreateDirectory(nativeDir);
        File.WriteAllBytes(Path.Combine(nativeDir, "NotDirectML.dll"), new byte[] { 0x00 });

        var ex = Record.Exception(() => DirectMlNative.Preload(mTempDir));
        Assert.Null(ex);
    }

    [Fact]
    public void Preload_DllUnderUnrelatedRidFolder_IsIgnored_DoesNotThrow()
    {
        // 把文件放进一个必然不是当前进程架构对应的 rid 目录，验证方法只按架构映射查找单一目录，
        // 不会遍历其他 rid（这里仅验证不抛，行为上等价于“未找到”）。
        var otherRid = CurrentRid == "win-x86" ? "win-arm64" : "win-x86";
        var nativeDir = Path.Combine(mTempDir, "runtimes", otherRid, "native");
        Directory.CreateDirectory(nativeDir);
        File.WriteAllBytes(Path.Combine(nativeDir, "DirectML.dll"), new byte[] { 0x00 });

        var ex = Record.Exception(() => DirectMlNative.Preload(mTempDir));
        Assert.Null(ex);
    }
}