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

if (!(Test-Path $gamePluginFolder)) {
    throw "game plugin folder missing: $gamePluginFolder"
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

New-Item -ItemType Directory -Path $pluginOnlyStageDir | Out-Null
Copy-Item -LiteralPath $gamePluginFolder -Destination $pluginOnlyFolder -Recurse -Force

$pluginJunk = @(
    "CachedRoundSplashScreens",
    "Settings",
    "woop",
    "debug_profiles.json"
)

foreach ($junk in $pluginJunk) {
    $junkPath = Join-Path $pluginOnlyFolder $junk
    if (Test-Path $junkPath) {
        Remove-Item -LiteralPath $junkPath -Recurse -Force
    }
}

Get-ChildItem -LiteralPath $pluginOnlyFolder -File | Where-Object {
    $_.Name -like "debug_profiles*.json" -or ($_.Extension -eq ".bak" -and $_.Name -ne "localization.bak")
} | Remove-Item -Force

Get-ChildItem -LiteralPath $pluginOnlyFolder -Filter *.bak | Where-Object { $_.Name -ne "localization.bak" } | Remove-Item -Force

if ($BuildOutput -and (Test-Path $BuildOutput)) {
    $filesToReplace = @(
        "BetterFG.dll",
        "BetterFG.pdb",
        "uGUIship.dll",
        "uGUIship.pdb",
        "FallGuysLib.dll",
        "localization.bak"
    )

    foreach ($name in $filesToReplace) {
        $src = Join-Path $BuildOutput $name
        if (Test-Path $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $pluginOnlyFolder $name) -Force
        }
    }

    $libsDest = Join-Path $pluginOnlyFolder "Libs"
    $libsSource = Join-Path $BuildOutput "Libs"
    if (Test-Path $libsSource) {
        if (!(Test-Path $libsDest)) {
            New-Item -ItemType Directory -Path $libsDest | Out-Null
        }
        Copy-Item -Path (Join-Path $libsSource "*") -Destination $libsDest -Force
    }

    # older builds dumped the vendored dlls loose in the plugin root next to BetterFG.dll. they live
    # in Libs\ now, so clear any stragglers out of the stage or they ride along in the zip forever.
    if (Test-Path $libsDest) {
        $staleFgl = Join-Path $libsDest "FallGuysLib.dll"
        if (Test-Path $staleFgl) { Remove-Item -LiteralPath $staleFgl -Force }
        Get-ChildItem -LiteralPath $libsDest -Filter *.dll | ForEach-Object {
            $loose = Join-Path $pluginOnlyFolder $_.Name
            if (Test-Path $loose) {
                Remove-Item -LiteralPath $loose -Force
            }
        }
        $staleFgl = Join-Path $libsDest "FallGuysLib.dll"
        if (Test-Path $staleFgl) { Remove-Item -LiteralPath $staleFgl -Force }
    }
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
