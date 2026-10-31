param(
    [switch]$Silent
)

$stateFile = Join-Path $PSScriptRoot 'prompt_editor.state.json'
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
$form.Text = 'BettrFG Editor Build'
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true
$form.ClientSize = New-Object System.Drawing.Size(340, 210)
$script:container = $form
$script:form = $form
$script:y = 16

$editorRow = New-DestList 'Copy BetterFG.Creator.dll after build' (Get-Saved 'EditorCopy' $true) (Get-SavedRows 'EditorDests')

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
    EditorCopy  = $editorRow.Copy.Checked
    EditorDests = Get-AllDestRows $editorRow
}
$state | ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8

if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
    Write-Output ("EDITORCOPY:" + (B $editorRow.Copy.Checked))
    if ($editorRow.Copy.Checked) { foreach ($d in (Get-CheckedDests $editorRow)) { Write-Output "EDITORDEST:$d" } }
} else {
    Write-Output "EDITORCOPY:0"
}
