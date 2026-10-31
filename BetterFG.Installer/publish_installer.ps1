param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$CopyBuildsToDownloads
)

$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $projectRoot
$publishDir = Join-Path $projectRoot ("publish\" + $Configuration + "\" + $Runtime)
$propsPath = Join-Path $repoRoot "Directory.Build.props"

if (!(Test-Path $propsPath)) {
    throw "Directory.Build.props missing: $propsPath"
}

[xml]$propsXml = Get-Content $propsPath -Raw
$props = $propsXml.Project.PropertyGroup | Select-Object -First 1
$version = $props.BettrFGVersion
$downloadsDir = Join-Path ([Environment]::GetFolderPath("UserProfile")) "Downloads"
$installerExeName = "BettrFG.Installer.exe"
$downloadsExe = Join-Path $downloadsDir $installerExeName
$selfContainedFlag = if ($SelfContained) { "true" } else { "false" }
$singleFileArgs = @(
    "publish",
    (Join-Path $projectRoot "BetterFG.Installer.csproj"),
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedFlag,
    "-p:PublishSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:SkipInstallerPublish=true",
    "-o", $publishDir
)

if ($SelfContained) {
    $singleFileArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $singleFileArgs += "-p:EnableCompressionInSingleFile=true"
}

dotnet @singleFileArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishDir $installerExeName
if (!(Test-Path $publishedExe)) {
    throw "published installer exe missing: $publishedExe"
}

Write-Host "installer published to $publishDir"
Write-Host "self-contained: $selfContainedFlag"

$copyTo = if ($CopyBuildsToDownloads) { $downloadsDir } else { $null }

if ($copyTo) {
    if (!(Test-Path $copyTo)) {
        New-Item -ItemType Directory -Path $copyTo | Out-Null
    }

    & (Join-Path $projectRoot "close_installer.ps1")
    for ($i = 0; $i -lt 20 -and (Get-Process -Name "BettrFG.Installer" -ErrorAction SilentlyContinue); $i++) {
        Start-Sleep -Milliseconds 200
    }

    $destExe = Join-Path $copyTo $installerExeName
    $copied = $false
    for ($i = 0; $i -lt 10; $i++) {
        try {
            Copy-Item -LiteralPath $publishedExe -Destination $destExe -Force
            $copied = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 300
        }
    }

    if ($copied) {
        Write-Host "installer copied to $destExe"
    }
    else {
        throw "couldnt copy installer to $copyTo (still locked?). published copy is at $publishedExe"
    }
}
