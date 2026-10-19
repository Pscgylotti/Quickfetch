param([string]$Version = '0.0.3', [string]$RuntimeVersion = '10.0.13')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force $outputRoot | Out-Null
foreach ($flavor in @('framework-dependent', 'portable')) {
    $publishFolder = Join-Path $outputRoot ('publish-' + $flavor)
    $selfContained = if ($flavor -eq 'portable') { 'true' } else { 'false' }
    [string[]]$runtimeArguments = if ($flavor -eq 'portable') { @("-p:RuntimeFrameworkVersion=$RuntimeVersion") } else { @() }
    dotnet publish (Join-Path $projectRoot 'Quickfetch/Quickfetch.csproj') -c Release -r win-x64 --self-contained $selfContained "-p:Version=$Version" -p:IncludeNativeLibrariesForSelfExtract=true @runtimeArguments -o $publishFolder
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $flavor" }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'LICENSE'), (Join-Path $projectRoot 'CONTRIBUTORS.md'), (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishFolder
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $publishFolder -Recurse -Force
    $archive = Join-Path $outputRoot "Quickfetch-$Version-win-x64-$flavor.zip"
    $packageFiles = @((Join-Path $publishFolder 'Quickfetch.exe'), (Join-Path $publishFolder 'README.md'), (Join-Path $publishFolder 'LICENSE'), (Join-Path $publishFolder 'CONTRIBUTORS.md'), (Join-Path $publishFolder 'THIRD-PARTY-NOTICES.md'), (Join-Path $publishFolder 'docs'))
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $archive -Force
}
$hashes = Get-ChildItem -LiteralPath $outputRoot -Filter "Quickfetch-$Version-win-x64-*.zip" | Sort-Object Name | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
}
$hashes | Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding utf8
Get-ChildItem -LiteralPath $outputRoot -File | Select-Object Name, Length
