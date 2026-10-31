param(
    [Parameter(Mandatory = $true)][string[]]$Inputs,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = "Stop"
$Inputs = @($Inputs | ForEach-Object { $_ -split ',' })

$langs = New-Object System.Collections.Generic.List[string]
$rows = [ordered]@{}

foreach ($path in $Inputs) {
    if (!(Test-Path -LiteralPath $path)) { Write-Host "localization: $path missing, skipped"; continue }
    $lines = @(([IO.File]::ReadAllText($path) -replace "`r`n", "`n" -split "`n") | Where-Object { $_.Length -gt 0 })
    if ($lines.Count -eq 0) { continue }
    $head = $lines[0].Split("`t")
    foreach ($l in $head) { if (-not $langs.Contains($l)) { $langs.Add($l) } }
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $cols = $lines[$i].Split("`t")
        $key = $cols[0]
        if ($key.Length -eq 0) { continue }
        if (-not $rows.Contains($key)) { $rows[$key] = @{} }
        for ($c = 0; $c -lt $head.Count; $c++) {
            if ($c + 1 -lt $cols.Count -and $cols[$c + 1].Length -gt 0) { $rows[$key][$head[$c]] = $cols[$c + 1] }
        }
    }
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append(($langs -join "`t")).Append("`n")
foreach ($key in $rows.Keys) {
    $vals = foreach ($l in $langs) { if ($rows[$key].ContainsKey($l)) { $rows[$key][$l] } else { "" } }
    [void]$sb.Append($key).Append("`t").Append(($vals -join "`t")).Append("`n")
}
$text = $sb.ToString()

$dir = Split-Path -Parent $Out
if ($dir -and !(Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
if ((Test-Path -LiteralPath $Out) -and [IO.File]::ReadAllText($Out) -eq $text) { return }
[IO.File]::WriteAllText($Out, $text, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "localization: wrote $Out ($($rows.Count) keys, $($langs -join '/'))"
