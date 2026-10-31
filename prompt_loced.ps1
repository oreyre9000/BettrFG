param(
    [switch]$Silent
)

$stateFile = Join-Path $PSScriptRoot 'prompt_loced.state.json'
$saved = $null
if (Test-Path $stateFile) {
    try { $saved = Get-Content $stateFile -Raw | ConvertFrom-Json } catch {}
}
function Get-Saved([string]$name, $default) {
    if ($saved -and ($saved.PSObject.Properties.Name -contains $name)) { return $saved.$name }
    return $default
}

. (Join-Path $PSScriptRoot 'prompt_destlist.ps1')

$form = New-Object System.Windows.Forms.Form
$form.Text = 'BettrFG LocalizationEditor Build'
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true
$form.ClientSize = New-Object System.Drawing.Size(340, 240)
$script:container = $form
$script:form = $form
$script:y = 16

$defaultRows = Get-SavedRows 'LocedDests'
if ($defaultRows.Count -eq 0) {
    $defaultRows = @([pscustomobject]@{ Enabled = $true; Path = (Join-Path ([Environment]::GetFolderPath("UserProfile")) "Downloads") })
}
$locedRow = New-DestList 'Copy LocalizationEditor after build' (Get-Saved 'LocedCopy' $true) $defaultRows
$cbLocedKill = New-Check 'Kill and restart LocalizationEditor after build' (Get-Saved 'LocedKill' $false)

$ok = New-Object System.Windows.Forms.Button
$ok.Text = 'Build'
$ok.Location = New-Object System.Drawing.Point(60, ($script:y + 8))
$ok.Size = New-Object System.Drawing.Size(100, 32)
$ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
$form.Controls.Add($ok)
$form.AcceptButton = $ok

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'Compile only'
$cancel.Location = New-Object System.Drawing.Point(180, ($script:y + 8))
$cancel.Size = New-Object System.Drawing.Size(100, 32)
$cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($cancel)
$form.CancelButton = $cancel

$form.ClientSize = New-Object System.Drawing.Size(340, ($script:y + 56))

$result = if ($Silent) { [System.Windows.Forms.DialogResult]::OK } else { $form.ShowDialog() }

function B([bool]$v) { if ($v) { '1' } else { '0' } }

$state = @{
    LocedCopy  = $locedRow.Copy.Checked
    LocedDests = Get-AllDestRows $locedRow
    LocedKill  = $cbLocedKill.Checked
}
$state | ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8

if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
    Write-Output ("LOCEDCOPY:" + (B $locedRow.Copy.Checked))
    if ($locedRow.Copy.Checked) { foreach ($d in (Get-CheckedDests $locedRow)) { Write-Output "LOCEDDEST:$d" } }
    Write-Output ("LOCEDKILL:" + (B $cbLocedKill.Checked))
} else {
    Write-Output "LOCEDCOPY:0"
    Write-Output "LOCEDKILL:0"
}
