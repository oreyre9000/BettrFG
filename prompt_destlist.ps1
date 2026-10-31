Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace BfgDeploy
{
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    internal class FileOpenDialog { }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    public static class FolderPicker
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        public static string Pick(IntPtr owner, string title, string startPath)
        {
            IFileDialog dlg = (IFileDialog)(new FileOpenDialog());
            dlg.SetOptions(0x20 | 0x40 | 0x800 | 0x8);
            dlg.SetTitle(title);

            if (!string.IsNullOrEmpty(startPath) && System.IO.Directory.Exists(startPath))
            {
                Guid shellItemGuid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                IShellItem start;
                SHCreateItemFromParsingName(startPath, IntPtr.Zero, ref shellItemGuid, out start);
                dlg.SetFolder(start);
            }

            if (dlg.Show(owner) != 0) { return null; }

            IShellItem item;
            dlg.GetResult(out item);
            IntPtr buf;
            item.GetDisplayName(0x80058000, out buf);
            string path = Marshal.PtrToStringUni(buf);
            Marshal.FreeCoTaskMem(buf);
            return path;
        }
    }
}
'@

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

function New-DestList([string]$copyLabel, [bool]$copyChecked, [object[]]$rows) {
    $cb = New-Check $copyLabel $copyChecked

    $list = New-Object System.Windows.Forms.CheckedListBox
    $list.Location = New-Object System.Drawing.Point(14, $script:y)
    $list.Size = New-Object System.Drawing.Size(300, 90)
    $list.CheckOnClick = $true
    $list.IntegralHeight = $false
    $list.HorizontalScrollbar = $true
    foreach ($r in $rows) { [void]$list.Items.Add($r.Path, $r.Enabled) }
    $script:container.Controls.Add($list)
    $script:y += 96

    $add = New-Object System.Windows.Forms.Button
    $add.Text = 'Add folder...'
    $add.Location = New-Object System.Drawing.Point(14, $script:y)
    $add.Size = New-Object System.Drawing.Size(110, 26)
    $capturedList = $list
    $capturedForm = $script:form
    $add.Add_Click({
        $seed = ''
        if ($capturedList.SelectedIndex -ge 0) { $seed = [string]$capturedList.Items[$capturedList.SelectedIndex] }
        elseif ($capturedList.Items.Count -gt 0) { $seed = [string]$capturedList.Items[0] }
        $p = [BfgDeploy.FolderPicker]::Pick($capturedForm.Handle, 'Pick a destination folder', $seed)
        if ($p) {
            $dupe = $false
            foreach ($i in $capturedList.Items) { if ($i -eq $p) { $dupe = $true } }
            if (-not $dupe) { [void]$capturedList.Items.Add($p, $true) }
        }
    }.GetNewClosure())
    $script:container.Controls.Add($add)

    $remove = New-Object System.Windows.Forms.Button
    $remove.Text = 'Remove'
    $remove.Location = New-Object System.Drawing.Point(132, $script:y)
    $remove.Size = New-Object System.Drawing.Size(90, 26)
    $remove.Add_Click({ if ($capturedList.SelectedIndex -ge 0) { $capturedList.Items.RemoveAt($capturedList.SelectedIndex) } }.GetNewClosure())
    $script:container.Controls.Add($remove)

    $script:y += 34
    return @{ Copy = $cb; List = $list }
}

function Get-CheckedDests($destList) {
    $out = @()
    for ($i = 0; $i -lt $destList.List.Items.Count; $i++) {
        if ($destList.List.GetItemChecked($i)) { $out += [string]$destList.List.Items[$i] }
    }
    return @($out)
}

function Get-AllDestRows($destList) {
    $out = @()
    for ($i = 0; $i -lt $destList.List.Items.Count; $i++) {
        $flag = if ($destList.List.GetItemChecked($i)) { '1' } else { '0' }
        $out += "$flag|$([string]$destList.List.Items[$i])"
    }
    return ,$out
}

function Get-SavedRows([string]$name) {
    $raw = @(Get-Saved $name @())
    $out = @()
    foreach ($line in $raw) {
        $t = [string]$line
        $enabled = $true
        if ($t -match '^([01])\|(.+)$') { $enabled = $Matches[1] -eq '1'; $t = $Matches[2] }
        if (-not [System.IO.Path]::IsPathRooted($t)) { continue }
        $out += [pscustomobject]@{ Enabled = $enabled; Path = $t }
    }
    return @($out)
}
