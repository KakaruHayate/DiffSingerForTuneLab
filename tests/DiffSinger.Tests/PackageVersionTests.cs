using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace DiffSinger.Tests;

// 本 PR 的核心改动是依赖版本升级：
//   Microsoft.ML.OnnxRuntime.DirectML  1.20.1 → 1.23.0（四个项目：插件、MLRuntime 子进程、
//     OpenUtau.Core 门面、冒烟测试——四者必须同版本，见各 csproj 注释：子进程模型 / 外部音素器
//     DLL 绑定均假设同一份 onnxruntime.dll）；
//   Microsoft.AI.DirectML               1.15.2 → 1.15.4（仅插件项目，随包分发 DirectML.dll 的来源）。
// 相关 .cs 文件（RuntimeHost.cs / DiffSingerModels.cs / DirectMlNative.cs）仅注释中的版本号随之更新，
// 无逻辑改动。这里直接解析磁盘上的 .csproj 校验版本号确实落地、且四项目版本保持一致，
// 并防止升级被静默回滚到已知有缺陷的旧版本。
public class PackageVersionTests
{
    const string ExpectedOnnxRuntimeVersion = "1.23.0";
    const string ExpectedDirectMLNativeVersion = "1.15.4";

    static readonly string RepoRoot = FindRepoRoot();

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DiffSingerForTuneLab.csproj")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("无法从测试输出目录向上定位到仓库根（未找到 DiffSingerForTuneLab.csproj）。");
        return dir.FullName;
    }

    static XElement LoadProject(string csprojRelativePath) =>
        XDocument.Load(Path.Combine(RepoRoot, csprojRelativePath)).Root!;

    static string? GetPackageVersion(string csprojRelativePath, string packageId)
    {
        var root = LoadProject(csprojRelativePath);
        var ns = root.Name.Namespace;
        return root.Descendants(ns + "PackageReference")
            .Where(e => (string?)e.Attribute("Include") == packageId)
            .Select(e => (string?)e.Attribute("Version"))
            .FirstOrDefault();
    }

    // 四个共享同一 onnxruntime 包身份的项目（相对仓库根路径）。
    public static IEnumerable<object[]> OnnxRuntimeDirectMLProjectPaths()
    {
        yield return new object[] { "DiffSingerForTuneLab.csproj" };
        yield return new object[] { Path.Combine("MLRuntime", "MLRuntime.csproj") };
        yield return new object[] { Path.Combine("OpenUtauFacade", "OpenUtau.Core.csproj") };
        yield return new object[] { Path.Combine("tools", "DiffSingerSmokeTest", "DiffSingerSmokeTest.csproj") };
    }

    [Theory]
    [MemberData(nameof(OnnxRuntimeDirectMLProjectPaths))]
    public void Project_FileExists(string csprojRelativePath)
    {
        // 前置断言：若路径本身失效，其余测试的 XDocument.Load 会抛 FileNotFoundException，
        // 单独断言可给出更明确的失败原因。
        Assert.True(File.Exists(Path.Combine(RepoRoot, csprojRelativePath)),
            $"未找到项目文件：{csprojRelativePath}");
    }

    [Theory]
    [MemberData(nameof(OnnxRuntimeDirectMLProjectPaths))]
    public void Project_ReferencesExpectedOnnxRuntimeDirectMLVersion(string csprojRelativePath)
    {
        var version = GetPackageVersion(csprojRelativePath, "Microsoft.ML.OnnxRuntime.DirectML");
        Assert.Equal(ExpectedOnnxRuntimeVersion, version);
    }

    [Theory]
    [MemberData(nameof(OnnxRuntimeDirectMLProjectPaths))]
    public void Project_DoesNotReferenceStalePreUpgradeOnnxRuntimeVersion(string csprojRelativePath)
    {
        // 回归测试：防止升级被静默回滚到已知有缺陷的旧版本
        //（1.20.1 的 CPU EP 扩展层图优化在 DiffSinger 声学图上原生崩溃；
        //  1.20 的 DML EP 缺 AppendExecutionProvider_DML 所需、DirectML 1.15 才引入的 API）。
        var version = GetPackageVersion(csprojRelativePath, "Microsoft.ML.OnnxRuntime.DirectML");
        Assert.NotNull(version);
        Assert.NotEqual("1.20.1", version);
        Assert.DoesNotContain("1.20", version!, StringComparison.Ordinal);
    }

    [Fact]
    public void AllProjectsReferencingOnnxRuntimeDirectML_UseTheSameVersion()
    {
        // 版本不一致会破坏子进程模型（MLRuntime.exe 经 IPC 与插件通信、假设同一份 onnxruntime.dll）
        // 以及 OpenUtau.Core 门面对外部声库自带音素器 DLL 的绑定假设。
        var versions = OnnxRuntimeDirectMLProjectPaths()
            .Select(row => (string)row[0])
            .Select(rel => GetPackageVersion(rel, "Microsoft.ML.OnnxRuntime.DirectML"))
            .ToList();

        Assert.All(versions, Assert.NotNull);
        Assert.Single(versions.Distinct());
        Assert.Equal(ExpectedOnnxRuntimeVersion, versions[0]);
    }

    [Fact]
    public void MainPlugin_ReferencesExpectedDirectMLNativeVersion()
    {
        var version = GetPackageVersion("DiffSingerForTuneLab.csproj", "Microsoft.AI.DirectML");
        Assert.Equal(ExpectedDirectMLNativeVersion, version);
    }

    [Fact]
    public void MainPlugin_DoesNotReferenceStalePreUpgradeDirectMLNativeVersion()
    {
        var version = GetPackageVersion("DiffSingerForTuneLab.csproj", "Microsoft.AI.DirectML");
        Assert.NotEqual("1.15.2", version);
    }

    [Fact]
    public void MainPlugin_DirectMLPackageReference_HasGeneratePathPropertyEnabled()
    {
        // CopyDirectMLNative target 依赖 $(PkgMicrosoft_AI_DirectML)（由 GeneratePathProperty 生成）
        // 把 DirectML.dll 拷进 runtimes/win-x64/native/。此属性一旦被误删，拷贝会静默失效
        // （变量为空 → SourceFiles 路径不存在，MSBuild 默认不因此报错）。
        var root = LoadProject("DiffSingerForTuneLab.csproj");
        var ns = root.Name.Namespace;
        var element = root.Descendants(ns + "PackageReference")
            .First(e => (string?)e.Attribute("Include") == "Microsoft.AI.DirectML");
        Assert.Equal("true", (string?)element.Attribute("GeneratePathProperty"));
    }

    [Theory]
    [InlineData("RuntimeHost.cs")]
    [InlineData("DiffSingerModels.cs")]
    [InlineData("DirectMlNative.cs")]
    public void SourceComments_DoNotReferenceStaleOnnxRuntimeVersion(string relativeCsFile)
    {
        // 这三个文件在本 PR 中只更新了注释里引用的 onnxruntime 版本号（1.20/1.20.1 → 1.23/1.23.x），
        // 无逻辑改动；确保没有遗漏、残留旧版本号误导后续维护者。
        var text = File.ReadAllText(Path.Combine(RepoRoot, relativeCsFile));
        Assert.DoesNotContain("1.20", text, StringComparison.Ordinal);
    }
}