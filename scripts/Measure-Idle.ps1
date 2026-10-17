param([int]$Seconds = 15, [int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
if (-not ('QuickfetchIdleAudit' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class QuickfetchIdleAudit {
 [StructLayout(LayoutKind.Sequential)] public struct Counters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool GetProcessIoCounters(IntPtr process, out Counters counters);
}
'@
}
$observedApp = if ($ProcessId) { Get-Process -Id $ProcessId } else { Get-Process Quickfetch | Select-Object -First 1 }
$beforeIo = New-Object QuickfetchIdleAudit+Counters
if (-not [QuickfetchIdleAudit]::GetProcessIoCounters($observedApp.Handle, [ref]$beforeIo)) { throw 'Cannot read process I/O counters' }
$beforeCpu = $observedApp.TotalProcessorTime.TotalMilliseconds
Start-Sleep -Seconds $Seconds
$observedApp.Refresh()
$afterIo = New-Object QuickfetchIdleAudit+Counters
[QuickfetchIdleAudit]::GetProcessIoCounters($observedApp.Handle, [ref]$afterIo) | Out-Null
[pscustomobject]@{
    Seconds = $Seconds
    CpuMilliseconds = $observedApp.TotalProcessorTime.TotalMilliseconds - $beforeCpu
    ReadOperations = $afterIo.ReadOperations - $beforeIo.ReadOperations
    WriteOperations = $afterIo.WriteOperations - $beforeIo.WriteOperations
    ReadBytes = $afterIo.ReadBytes - $beforeIo.ReadBytes
    WriteBytes = $afterIo.WriteBytes - $beforeIo.WriteBytes
    WorkingSetMiB = [math]::Round($observedApp.WorkingSet64 / 1MB, 1)
    PrivateMiB = [math]::Round($observedApp.PrivateMemorySize64 / 1MB, 1)
} | ConvertTo-Json
