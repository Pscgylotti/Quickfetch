param([Parameter(Mandatory=$true)][string]$RuntimeDirectory)
$ErrorActionPreference = 'Stop'
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$identity = Join-Path $runtime 'Quickfetch.WindowsAI.Ocr.identity.msix'
$signed = Test-Path -LiteralPath $identity
if (-not $signed) {
    $development = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
    if ($development.AllowDevelopmentWithoutDevLicense -ne 1) { throw 'A signed Quickfetch Windows AI OCR identity package is required. Developer mode and certificate trust will not be changed.' }
}
# Windows refuses to move an existing external-location package in place.
# Relocate only this companion's identity, never TixTab or Quickfetch's user data.
foreach ($existing in @(Get-AppxPackage -Name Quickfetch.WindowsAI.Ocr | Where-Object { $_.Publisher -eq 'CN=Quickfetch' })) {
    if (-not $existing.InstallLocation.TrimEnd('\').Equals($runtime.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { Remove-AppxPackage -Package $existing.PackageFullName }
}
if ($signed) {
    Add-AppxPackage -Path $identity -ExternalLocation $runtime
} else {
    Add-AppxPackage -Register (Join-Path $runtime 'AppxManifest.xml') -ExternalLocation $runtime
}
