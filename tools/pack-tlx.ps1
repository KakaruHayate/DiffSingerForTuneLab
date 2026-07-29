# Build one RID-specific .tlx package. The package root contains manifest.json, managed assemblies,
# runtimes/<rid>/native/, and the framework-dependent MLRuntime apphost under mlruntime/.
# Usage: pwsh tools/pack-tlx.ps1 [-Configuration Release] [-RuntimeIdentifier win-x64|osx-arm64|linux-x64]
param(
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "osx-arm64", "linux-x64")]
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

$hostOperatingSystem = if (
    [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
) {
    "win"
} elseif (
    [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::OSX)
) {
    "osx"
} elseif (
    [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Linux)
) {
    "linux"
} else {
    throw "Unsupported packaging host: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)"
}
$targetOperatingSystem = $RuntimeIdentifier.Split("-")[0]
if ($targetOperatingSystem -ne $hostOperatingSystem) {
    throw "Package $RuntimeIdentifier on a $targetOperatingSystem host; current host is $hostOperatingSystem. Unix packages must be created on Unix so MLRuntime retains executable permissions."
}

$repo = Split-Path $PSScriptRoot -Parent
$runId = [Guid]::NewGuid().ToString("N")
$artifacts = Join-Path $repo "artifacts/tlx/$RuntimeIdentifier/$runId"
$pluginPublish = Join-Path $artifacts "plugin-publish"
$source = Join-Path $artifacts "package"
$mlSource = Join-Path $artifacts "mlruntime-publish"
$mlStage = Join-Path $source "mlruntime"
$out = Join-Path $PSScriptRoot "tlx"

New-Item -ItemType Directory -Force -Path $pluginPublish, $source, $mlSource | Out-Null

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

# Run one pack script per clean checkout/runner. Do not launch multiple RIDs concurrently in the same worktree,
# because NuGet restore shares project obj files.
Invoke-DotNet restore (Join-Path $repo "DiffSingerForTuneLab.csproj") -r $RuntimeIdentifier "-p:NuGetAudit=false"
Invoke-DotNet restore (Join-Path $repo "MLRuntime/MLRuntime.csproj") -r $RuntimeIdentifier "-p:NuGetAudit=false"
Invoke-DotNet publish (Join-Path $repo "DiffSingerForTuneLab.csproj") `
    -c $Configuration -r $RuntimeIdentifier --self-contained false --no-restore -o $pluginPublish
Invoke-DotNet publish (Join-Path $repo "MLRuntime/MLRuntime.csproj") `
    -c $Configuration -r $RuntimeIdentifier --self-contained false --no-restore -o $mlSource

# Build the package tree from publish outputs instead of deleting unwanted files in place.
# Managed/plugin content excludes flattened native assets; those are restored to runtimes/<rid>/native below.
$nativeFileNames = @(
    "DirectML.dll", "DirectML.pdb", "DirectML.Debug.dll", "DirectML.Debug.pdb",
    "onnxruntime.dll", "onnxruntime.lib", "onnxruntime_providers_shared.dll",
    "onnxruntime_providers_shared.lib", "libonnxruntime.dylib", "libonnxruntime.so"
)
Get-ChildItem $pluginPublish | Where-Object { $_.Name -notin $nativeFileNames } |
    Copy-Item -Destination $source -Recurse -Force

# Stage MLRuntime below the plugin, excluding its duplicate flattened ONNX native assets.
New-Item -ItemType Directory -Force -Path $mlStage | Out-Null
Get-ChildItem $mlSource | Where-Object { $_.Name -notin $nativeFileNames -and $_.Name -ne "runtimes" } |
    Copy-Item -Destination $mlStage -Recurse -Force
# Each publish uses one explicit RID, so only that RID's native assets are present.
$native = Join-Path $source "runtimes/$RuntimeIdentifier/native"
New-Item -ItemType Directory -Force -Path $native | Out-Null

# RID publish flattens native assets into the publish root, while the plugin's deps.json retains their canonical
# runtimes/<rid>/native paths. Restore that layout for TuneLab's AssemblyDependencyResolver.
$nativeFiles = if ($RuntimeIdentifier -eq "win-x64") {
    @("DirectML.dll", "onnxruntime.dll", "onnxruntime.lib", "onnxruntime_providers_shared.dll", "onnxruntime_providers_shared.lib")
} elseif ($RuntimeIdentifier -eq "osx-arm64") {
    @("libonnxruntime.dylib")
} else {
    @("libonnxruntime.so")
}
foreach ($file in $nativeFiles) {
    $rootCopy = Join-Path $pluginPublish $file
    if (Test-Path $rootCopy) { Copy-Item $rootCopy (Join-Path $native $file) -Force }
}

$onnxNativeName = if ($RuntimeIdentifier -eq "win-x64") { "onnxruntime.dll" }
    elseif ($RuntimeIdentifier -eq "osx-arm64") { "libonnxruntime.dylib" }
    else { "libonnxruntime.so" }
if (-not (Test-Path (Join-Path $native $onnxNativeName))) {
    throw "Missing ONNX Runtime native library: $onnxNativeName"
}
if ($RuntimeIdentifier -eq "win-x64" -and -not (Test-Path (Join-Path $native "DirectML.dll"))) {
    throw "Missing DirectML.dll in Windows package"
}


# A RID-specific package advertises only the RID it actually contains.
$manifestPath = Join-Path $source "manifest.json"
$manifest = Get-Content $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
$manifest.platforms = @($RuntimeIdentifier)
$manifestJson = $manifest | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, [System.Text.UTF8Encoding]::new($false))

$runtimeName = if ($RuntimeIdentifier -eq "win-x64") { "MLRuntime.exe" } else { "MLRuntime" }
$runtimePath = Join-Path $mlStage $runtimeName
if (-not (Test-Path $runtimePath)) { throw "Missing MLRuntime apphost: $runtimePath" }
if ($RuntimeIdentifier -ne "win-x64") {
    chmod +x $runtimePath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to mark MLRuntime executable: $runtimePath"
    }
}

New-Item -ItemType Directory -Force -Path $out | Out-Null
$tlx = Join-Path $out ("$($manifest.id)-$($manifest.version)-$RuntimeIdentifier.tlx")
if (Test-Path $tlx) {
    $tlx = Join-Path $out ("$($manifest.id)-$($manifest.version)-$RuntimeIdentifier-$runId.tlx")
}
[System.IO.Compression.ZipFile]::CreateFromDirectory($source, $tlx)

Write-Host "Packed $tlx"
