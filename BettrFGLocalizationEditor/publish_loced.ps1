param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$publishDir = Join-Path $projectRoot ("publish\" + $Configuration + "\" + $Runtime)

dotnet publish (Join-Path $projectRoot "BettrFGLocalizationEditor.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:SkipLocedPrompt=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishDir "BettrFGLocalizationEditor.exe"
if (!(Test-Path $publishedExe)) {
    throw "published localization editor exe missing: $publishedExe"
}

Write-Host "loc editor published to $publishDir"
