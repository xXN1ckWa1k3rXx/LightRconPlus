param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$mode = if ($SelfContained) { 'true' } else { 'false' }
$buildArtifacts = Join-Path $projectRoot 'artifacts\build'
foreach ($target in @('Desktop', 'Host')) {
    $destination = Join-Path $projectRoot "artifacts\$($target.ToLowerInvariant())"
    & dotnet publish (Join-Path $projectRoot "src\LightRcon.$target") --artifacts-path $buildArtifacts -c Release -r win-x64 --self-contained $mode -o $destination -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publishing $target failed." }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $projectRoot 'artifacts\README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $projectRoot 'artifacts\THIRD-PARTY-NOTICES.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $projectRoot 'artifacts\LICENSE')
foreach ($target in @('', 'desktop', 'host')) {
    $packageDirectory = Join-Path $projectRoot "artifacts\$target"
    foreach ($notice in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CONTRIBUTING.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination (Join-Path $packageDirectory $notice)
    }
    $packageDocs = Join-Path $packageDirectory 'docs'
    $null = New-Item -ItemType Directory -Path $packageDocs -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\VALIDATION.md') -Destination (Join-Path $packageDocs 'VALIDATION.md')
}
Write-Host 'Published desktop and console programs to artifacts/.'
