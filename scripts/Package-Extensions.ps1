param([string]$Version = '0.0.5')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
foreach ($browser in @('edge','chrome','firefox')) {
    $destination = Join-Path $root "artifacts/extension-$browser-$Version"
    New-Item -ItemType Directory -Force $destination | Out-Null
    foreach ($file in @('background.js','popup.html','popup.js','README.txt')) {
        Copy-Item -LiteralPath (Join-Path $root "browser-extension/$file") -Destination $destination -Force
    }
    $sourceManifest = if ($browser -eq 'firefox') { 'firefox-manifest.json' } else { 'manifest.json' }
    $manifest = Get-Content -LiteralPath (Join-Path $root "browser-extension/$sourceManifest") -Raw | ConvertFrom-Json
    $manifest.version = $Version
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
    Compress-Archive -LiteralPath @((Join-Path $destination 'manifest.json'), (Join-Path $destination 'background.js'), (Join-Path $destination 'popup.html'), (Join-Path $destination 'popup.js'), (Join-Path $destination 'README.txt')) -DestinationPath (Join-Path $root "artifacts/Quickfetch-$Version-$browser-extension.zip") -Force
}
