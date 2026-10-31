param(
    [string]$GameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Fall Guys",
    [string]$BuildOutput = "",
    [switch]$CopyBuildsToDownloads
)

$ErrorActionPreference = "Stop"

$installerRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerRoot
$payloadDir = Join-Path $installerRoot "payload"
$pluginOnlyStageDir = Join-Path $installerRoot "plugin_only_stage"
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$pluginOnlyFolder = Join-Path $pluginOnlyStageDir "BetterFG"
$gamePluginFolder = Join-Path $GameRoot "BepInEx\plugins\BetterFG"
$BuildOutput = $BuildOutput.Trim('"')

if (!(Test-Path $propsPath)) {
    throw "Directory.Build.props missing: $propsPath"
}

[xml]$propsXml = Get-Content $propsPath -Raw
$props = $propsXml.Project.PropertyGroup | Select-Object -First 1
$modVersion = $props.BettrFGVersion
$displayName = $props.BettrFGDisplayName

if ([string]::IsNullOrWhiteSpace($modVersion)) {
    throw "couldnt read BettrFGVersion from Directory.Build.props"
}

if ([string]::IsNullOrWhiteSpace($displayName)) {
    $displayName = "BettrFG"
}

$releaseZipName = "bettrfg_plugin.zip"
$legacyReleaseZipName = "betterfg_plugin.zip"
$dottedZipName = "$displayName.$modVersion.zip"

$pluginZipPath = Join-Path $payloadDir $releaseZipName
$legacyPluginZipPath = Join-Path $payloadDir $legacyReleaseZipName
$dottedPluginZipPath = Join-Path $payloadDir $dottedZipName

$downloadsDir = Join-Path ([Environment]::GetFolderPath("UserProfile")) "Downloads"
$downloadsPluginZipPath = Join-Path $downloadsDir $releaseZipName

$staleBepInEx = @(
    (Join-Path $payloadDir "BepInEx.zip"),
    (Join-Path $downloadsDir "BepInEx.zip")
)
foreach ($stale in $staleBepInEx) {
    if (Test-Path $stale) {
        Remove-Item -LiteralPath $stale -Force
    }
}

if (!(Test-Path $payloadDir)) {
    New-Item -ItemType Directory -Path $payloadDir | Out-Null
}

if (Test-Path $pluginOnlyStageDir) {
    Remove-Item -LiteralPath $pluginOnlyStageDir -Recurse -Force
}

if (!$BuildOutput -or !(Test-Path $BuildOutput)) {
    throw "build output missing: $BuildOutput"
}

New-Item -ItemType Directory -Path $pluginOnlyFolder | Out-Null
foreach ($name in @("BetterFG.dll", "BetterFG.pdb", "FallGuysLib.dll")) {
    Copy-Item -LiteralPath (Join-Path $BuildOutput $name) -Destination (Join-Path $pluginOnlyFolder $name) -Force
}

$libsDest = Join-Path $pluginOnlyFolder "Libs"
New-Item -ItemType Directory -Path $libsDest | Out-Null
foreach ($name in @("NAudio.dll", "NAudio.Core.dll", "NAudio.WinMM.dll", "NAudio.Wasapi.dll")) {
    Copy-Item -LiteralPath (Join-Path $BuildOutput $name) -Destination (Join-Path $libsDest $name) -Force
}

foreach ($old in @($pluginZipPath, $legacyPluginZipPath, $dottedPluginZipPath)) {
    if (Test-Path $old) {
        Remove-Item -LiteralPath $old -Force
    }
}

Compress-Archive -Path (Join-Path $pluginOnlyStageDir "*") -DestinationPath $pluginZipPath -CompressionLevel Optimal
Copy-Item -LiteralPath $pluginZipPath -Destination $legacyPluginZipPath -Force
Copy-Item -LiteralPath $pluginZipPath -Destination $dottedPluginZipPath -Force

Write-Host "plugin zip rebuilt at $pluginZipPath"
Write-Host "payload zips: $legacyPluginZipPath and $dottedPluginZipPath"

if ($CopyBuildsToDownloads) {
    if (!(Test-Path $downloadsDir)) {
        New-Item -ItemType Directory -Path $downloadsDir | Out-Null
    }
    Copy-Item -LiteralPath $pluginZipPath -Destination $downloadsPluginZipPath -Force
    Write-Host "copied $releaseZipName to $downloadsDir"
}
