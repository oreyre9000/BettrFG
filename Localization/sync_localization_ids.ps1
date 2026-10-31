param(
    [string]$BakPath = "$PSScriptRoot\bettrfg.bak",
    [string[]]$ScanDirs = @("$PSScriptRoot\..\BetterFG", "$PSScriptRoot\..\BetterFG.uGUI"),
    [string[]]$KnownPaths = @("$PSScriptRoot\shared.bak"),
    [string]$Prefixes = "ui|tweak|rpc|credits|leaderboard|sidewheel"
)

$ErrorActionPreference = "Stop"
$ScanDirs = @($ScanDirs | ForEach-Object { $_ -split ',' })
$KnownPaths = @($KnownPaths | ForEach-Object { $_ -split ',' })

# every "ui.xxx" / "tweak.xxx" string literal in source IS a localization id by convention
# (see the localization-bak-format note) - scan for them rather than tracking call sites by hand.
# a handful of "tweak.*"/"ui.*"-shaped strings are NOT display ids though - BfgPatchGate keys and
# SettingsService storage keys share the naming convention but are never shown on screen, so a line
# that's purely one of those calls is skipped (an id used ANYWHERE else still counts).
$idPattern = '"((?:' + $Prefixes + ')\.[A-Za-z0-9_]+)"'
$nonDisplayLine = 'BfgPatchGate\(|SettingsService\.(Get|Set|Remove)\('

$foundIds = New-Object System.Collections.Generic.HashSet[string]
foreach ($dir in $ScanDirs) {
    if (!(Test-Path $dir)) { continue }
    Get-ChildItem -Path $dir -Filter *.cs -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object {
            foreach ($line in (Get-Content $_.FullName)) {
                if ($line -match $nonDisplayLine) { continue }
                foreach ($m in [regex]::Matches($line, $idPattern)) {
                    [void]$foundIds.Add($m.Groups[1].Value)
                }
            }
        }
}

if (!(Test-Path $BakPath)) {
    Write-Host "localization: no existing $BakPath, skipping sync"
    return
}

$raw = Get-Content -LiteralPath $BakPath -Raw
$lines = @(($raw -replace "`r`n", "`n" -split "`n") | Where-Object { $_.Length -gt 0 })
if ($lines.Count -eq 0) { Write-Host "localization: empty bak, skipping sync"; return }

$langs = $lines[0].Split("`t")
$existingKeys = New-Object System.Collections.Generic.HashSet[string]
for ($i = 1; $i -lt $lines.Count; $i++) {
    $key = $lines[$i].Split("`t")[0]
    if ($key.Length -gt 0) { [void]$existingKeys.Add($key) }
}
foreach ($known in $KnownPaths) {
    if (!(Test-Path -LiteralPath $known)) { continue }
    foreach ($l in [IO.File]::ReadAllLines($known)) { [void]$existingKeys.Add($l.Split("`t")[0]) }
}

$missing = @($foundIds | Where-Object { -not $existingKeys.Contains($_) -and -not $_.EndsWith('_') } | Sort-Object)
if ($missing.Count -eq 0) { Write-Host "localization: no new ids"; return }

function Guess-English([string]$id) {
    $body = $id -replace '^[a-z]+\.', ''
    $words = @($body.Split('_') | Where-Object { $_.Length -gt 0 } | ForEach-Object {
        $_.Substring(0,1).ToUpperInvariant() + $_.Substring(1)
    })
    return [string]::Join(' ', $words)
}

$newLines = @(foreach ($id in $missing) {
    $fields = @(for ($c = 0; $c -lt $langs.Count; $c++) {
        if ($langs[$c] -eq 'en') { Guess-English $id } else { "" }
    })
    ($id, ($fields -join "`t")) -join "`t"
})

Add-Content -LiteralPath $BakPath -Value $newLines -Encoding utf8
Write-Host "localization: added $($missing.Count) new id(s) - $($missing -join ', ')"
