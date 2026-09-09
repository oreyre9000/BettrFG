param(
    [switch]$Silent
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$stateFile = Join-Path $PSScriptRoot 'prompt_mod.state.json'
$saved = $null
if (Test-Path $stateFile) {
    try { $saved = Get-Content $stateFile -Raw | ConvertFrom-Json } catch {}
}
function Get-Saved([string]$name, $default) {
    if ($saved -and ($saved.PSObject.Properties.Name -contains $name)) { return $saved.$name }
    return $default
}

$form = New-Object System.Windows.Forms.Form
$form.Text = 'BettrFG Build'
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true
$form.ClientSize = New-Object System.Drawing.Size(340, 260)

$script:container = $form
$script:y = 16

function New-Check([string]$text, [bool]$checked) {
    $cb = New-Object System.Windows.Forms.CheckBox
    $cb.Text = $text
    $cb.Checked = $checked
    $cb.Location = New-Object System.Drawing.Point(14, $script:y)
    $cb.Size = New-Object System.Drawing.Size(300, 22)
    $script:container.Controls.Add($cb)
    $script:y += 28
    return $cb
}

function New-Drop([string]$text, [string[]]$items, [string]$selected) {
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = $text
    $lbl.Location = New-Object System.Drawing.Point(14, ($script:y + 3))
    $lbl.Size = New-Object System.Drawing.Size(60, 20)
    $script:container.Controls.Add($lbl)
    $dd = New-Object System.Windows.Forms.ComboBox
    $dd.DropDownStyle = 'DropDownList'
    $dd.Location = New-Object System.Drawing.Point(82, $script:y)
    $dd.Size = New-Object System.Drawing.Size(100, 22)
    [void]$dd.Items.AddRange($items)
    $dd.SelectedItem = $selected
    $script:container.Controls.Add($dd)
    $script:y += 30
    return $dd
}

$cbSteam = New-Check 'Copy plugin to Steam' (Get-Saved 'Steam' $true)
$cbEpic  = New-Check 'Copy plugin to Epic' (Get-Saved 'Epic' $true)
$cbDl    = New-Check 'Copy installer to Downloads' (Get-Saved 'Dl' $true)
$cbKill  = New-Check 'Kill and relaunch Fall Guys' (Get-Saved 'Kill' $true)
$cbEa    = New-Check 'Early access build' (Get-Saved 'Ea' $false)
$cbMach  = New-Check 'Compile the machinima editor' (Get-Saved 'Mach' $false)

$state = @{}
$collectors = @()
$hookDir = Join-Path $PSScriptRoot 'BetterFG\build'
if (Test-Path $hookDir) {
    foreach ($hook in (Get-ChildItem -Path $hookDir -Filter 'prompt.*.ps1' | Sort-Object Name)) { . $hook.FullName }
}

$ok = New-Object System.Windows.Forms.Button
$ok.Text = 'Build'
$ok.Location = New-Object System.Drawing.Point(60, ($script:y + 12))
$ok.Size = New-Object System.Drawing.Size(100, 32)
$ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
$form.Controls.Add($ok)
$form.AcceptButton = $ok

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'Compile only'
$cancel.Location = New-Object System.Drawing.Point(180, ($script:y + 12))
$cancel.Size = New-Object System.Drawing.Size(100, 32)
$cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($cancel)
$form.CancelButton = $cancel

$form.ClientSize = New-Object System.Drawing.Size(340, ($script:y + 60))

$result = if ($Silent) { [System.Windows.Forms.DialogResult]::OK } else { $form.ShowDialog() }

$modExtra = ''
foreach ($c in $collectors) { $modExtra += . $c }

$state['Steam'] = $cbSteam.Checked
$state['Epic']  = $cbEpic.Checked
$state['Dl']    = $cbDl.Checked
$state['Kill']  = $cbKill.Checked
$state['Ea']    = $cbEa.Checked
$state['Mach']  = $cbMach.Checked
$state | ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8

function B([bool]$v) { if ($v) { '1' } else { '0' } }

$ea = B $cbEa.Checked
$mach = B $cbMach.Checked

if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
    $steam = B $cbSteam.Checked
    $epic  = B $cbEpic.Checked
    $dl    = B $cbDl.Checked
    $kill  = B $cbKill.Checked
    Write-Output "BFGCHOICE:STEAM=$steam;EPIC=$epic;DL=$dl;KILL=$kill;EA=$ea;MACH=$mach;$modExtra"
} else {
    Write-Output "BFGCHOICE:STEAM=0;EPIC=0;DL=0;KILL=0;EA=$ea;MACH=$mach;$modExtra"
}
