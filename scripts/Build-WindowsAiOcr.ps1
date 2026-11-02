param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/windows-ai-ocr/runtime'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
dotnet publish (Join-Path $root 'native/windows-ai-ocr/Quickfetch.WindowsAI.Ocr.csproj') -c Release -o $output --disable-build-servers -m:1
if ($LASTEXITCODE -ne 0) { throw 'Windows AI OCR build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'native/windows-ai-ocr/AppxManifest.xml') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Register-WindowsAiOcr.ps1') -Destination $output -Force
$assets = Join-Path $output 'Assets'; New-Item -ItemType Directory -Path $assets -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::new((Join-Path $root 'Quickfetch/Assets/Quickfetch.ico'), 256, 256)
try {
    $original = $icon.ToBitmap()
    try {
        foreach ($entry in @(@('Logo',50),@('Square150',150),@('Square44',44))) {
            $bitmap = [Drawing.Bitmap]::new([int]$entry[1], [int]$entry[1]); $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic; $graphics.DrawImage($original,0,0,$bitmap.Width,$bitmap.Height); $bitmap.Save((Join-Path $assets ($entry[0]+'.png')), [Drawing.Imaging.ImageFormat]::Png) }
            finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $original.Dispose() }
} finally { $icon.Dispose() }
$licenses=Join-Path $output 'Licenses'; New-Item -ItemType Directory -Path $licenses -Force | Out-Null
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
foreach ($entry in @(@('microsoft.windowsappsdk.ai','1.8.79'),@('microsoft.windowsappsdk.foundation','1.8.260505001'))) {
    Copy-Item -LiteralPath (Join-Path $nuget ($entry[0]+'/'+$entry[1]+'/license.txt')) -Destination (Join-Path $licenses ($entry[0]+'.txt')) -Force
}
$dotnetRoot = Split-Path (Get-Command dotnet).Source -Parent
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination (Join-Path $licenses 'dotnet.txt') -Force
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $licenses 'dotnet-third-party.txt') -Force
