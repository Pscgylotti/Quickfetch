param([ValidateSet('status','on','off')][string]$Command, [string]$Executable = $env:QUICKFETCH_TOGGLE_EXE)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)

# Query limited process information also works when the running client is elevated.
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class V2Process {
    [DllImport("kernel32")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32", CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder s, ref int n);
    [DllImport("advapi32")] static extern bool OpenProcessToken(IntPtr p, int access, out IntPtr token);
    [DllImport("advapi32")] static extern bool GetTokenInformation(IntPtr token, int type, out int value, int size, out int used);
    [DllImport("kernel32")] static extern bool CloseHandle(IntPtr h);
    public static string PathOf(int pid) {
        var h=OpenProcess(4096,false,pid); if(h==IntPtr.Zero) return null;
        try { int n=32768; var s=new StringBuilder(n); return QueryFullProcessImageName(h,0,s,ref n) ? s.ToString() : null; }
        finally { CloseHandle(h); }
    }
    public static bool Elevated(int pid) {
        var h=OpenProcess(4096,false,pid); if(h==IntPtr.Zero) return false;
        try { IntPtr token; if(!OpenProcessToken(h,8,out token)) return false;
              try { int value,used; return GetTokenInformation(token,20,out value,4,out used) && value!=0; } finally { CloseHandle(token); } }
        finally { CloseHandle(h); }
    }
}
"@
function Clients { @(Get-Process -Name v2rayN -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq (Get-Process -Id $PID).SessionId }) }
function MatchingClient { Clients | Where-Object { [V2Process]::PathOf($_.Id) -eq $Executable } | Select-Object -First 1 }
function ReadMode {
    if (!(Test-Path -LiteralPath $config)) { return -1 }
    $raw = (Get-Content -Raw -LiteralPath $config -Encoding UTF8 | ConvertFrom-Json).SystemProxyItem.SysProxyType
    if ($null -eq $raw) { return -1 }
    switch ([string]$raw) { 'ForcedClear' {return 0} 'ForcedChange' {return 1} 'Unchanged' {return 2} 'Pac' {return 3} default {return [int]$raw} }
}
function SystemProxyEnabled {
    $v = Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name ProxyEnable -ErrorAction SilentlyContinue
    return ($v -eq 1)
}
function Reply($on, $available, $note) { [ordered]@{on=$on; available=$available; note=$note; executable=$Executable} | ConvertTo-Json -Compress }
function FindCombo($client) {
    if (!$client -or $client.HasExited) { return $null }
    $client.Refresh()
    $handle = $client.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) { return $null }
    $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
    $selector = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'cmbSystemProxy')
    return $window.FindFirst([Windows.Automation.TreeScope]::Descendants,$selector)
}
function SelectMode($combo, $index) {
    $list = $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $list.Expand()
    try {
        $selector = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)
        $items = $combo.FindAll([Windows.Automation.TreeScope]::Descendants,$selector)
        if ($items.Count -ne 4) { throw 'Unrecognized v2rayN system proxy menu.' }
        $items[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    } finally { if ($list.Current.ExpandCollapseState -eq 'Expanded') { $list.Collapse() } }
}
try {
    if (!$Executable) { $Executable = Clients | ForEach-Object { [V2Process]::PathOf($_.Id) } | Select-Object -First 1 }
    if (!$Executable -and $env:QUICKFETCH_SHORTCUT_FOLDER) {
        $link = Join-Path $env:QUICKFETCH_SHORTCUT_FOLDER 'v2rayN.lnk'
        if (Test-Path -LiteralPath $link -PathType Leaf) {
            $shell = New-Object -ComObject WScript.Shell
            $Executable = $shell.CreateShortcut($link).TargetPath
        }
    }
    if (!$Executable) {
        $Executable = Get-Command 'v2rayN.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    }
    if (!$Executable -or [IO.Path]::GetFileName($Executable) -ine 'v2rayN.exe' -or !(Test-Path -LiteralPath $Executable -PathType Leaf)) {
        Reply $null $false 'Choose v2rayN.exe in the tile options.'
        exit 0
    }
    $Executable = (Resolve-Path -LiteralPath $Executable).ProviderPath
    $dir = Split-Path -LiteralPath $Executable
    $config = Join-Path $dir 'guiConfigs\guiNConfig.json'
    $client = MatchingClient
    $mode = ReadMode
    $enabled = SystemProxyEnabled
    if ($Command -eq 'status') { Reply ([bool]($client -and $mode -eq 1 -and $enabled)) $true $Executable; exit 0 }
    $target = [int]($Command -eq 'on')
    if ($client -and $mode -eq $target -and $enabled -eq [bool]$target) { Reply ([bool]$target) $true $Executable; exit 0 }
    if (!$client) {
        Start-Process -FilePath $Executable -WorkingDirectory $dir
        for ($i=0; $i -lt 45 -and !$client; $i++) { Start-Sleep -Milliseconds 200; $client = MatchingClient }
        if (!$client) { throw 'v2rayN did not start.' }
    }
    if ([V2Process]::Elevated($client.Id) -and ![V2Process]::Elevated($PID)) {
        # Windows UI Automation requires matching integrity. Only elevate an explicit switch.
        $argsForHelper = "-NoProfile -NonInteractive -STA -ExecutionPolicy RemoteSigned -File ""$PSCommandPath"" $Command ""$Executable"""
        $helper = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $argsForHelper -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        if ($helper.ExitCode -ne 0) { throw 'The elevated v2rayN switch did not complete.' }
    } else {
        Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
        $combo = FindCombo $client
        if (!$combo) {
            # v2rayN's own single-instance activation brings a tray-only client into view.
            Start-Process -FilePath $Executable -WorkingDirectory $dir
            for ($i=0; $i -lt 50 -and !$combo; $i++) {
                Start-Sleep -Milliseconds 200
                $client = MatchingClient
                try { $combo = FindCombo $client } catch [Windows.Automation.ElementNotAvailableException] { $combo = $null }
            }
        }
        if (!$combo) { throw 'Could not find the system proxy selector in this v2rayN version.' }
        if ((ReadMode) -eq $target -and (SystemProxyEnabled) -ne [bool]$target) { SelectMode $combo 2; Start-Sleep -Milliseconds 150 }
        SelectMode $combo $target
    }
    for ($i=0; $i -lt 30; $i++) {
        if ((ReadMode) -eq $target -and (SystemProxyEnabled) -eq [bool]$target) { Reply ([bool]$target) $true $Executable; exit 0 }
        Start-Sleep -Milliseconds 200
    }
    throw 'v2rayN did not confirm the requested system proxy mode.'
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
