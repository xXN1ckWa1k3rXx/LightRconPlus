param([switch]$SelfContained, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$mode = if ($SelfContained) { 'true' } else { 'false' }
$buildArtifacts = Join-Path $projectRoot 'artifacts\build'
$packageRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $projectRoot 'artifacts\releases\Pre-alpha-1.0' } else { [System.IO.Path]::GetFullPath($OutputDirectory, $projectRoot) }
foreach ($target in @('Desktop', 'Host')) {
    $destination = Join-Path $packageRoot $target.ToLowerInvariant()
    & dotnet publish (Join-Path $projectRoot "src\LightRcon.$target") --artifacts-path $buildArtifacts -c Release -r win-x64 --self-contained $mode -o $destination -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publishing $target failed." }
}
foreach ($target in @('', 'desktop', 'host')) {
    $packageDirectory = Join-Path $packageRoot $target
    foreach ($notice in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CONTRIBUTING.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination (Join-Path $packageDirectory $notice)
    }
    $packageDocs = Join-Path $packageDirectory 'docs'
    $null = New-Item -ItemType Directory -Path $packageDocs -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\VALIDATION.md') -Destination (Join-Path $packageDocs 'VALIDATION.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\PLUGINS.md') -Destination (Join-Path $packageDocs 'PLUGINS.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\LUA-API.md') -Destination (Join-Path $packageDocs 'LUA-API.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\GAME-SETTINGS.md') -Destination (Join-Path $packageDocs 'GAME-SETTINGS.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\licenses') -Destination $packageDocs -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\gitbook') -Destination $packageDocs -Recurse -Force
    foreach ($folder in @('api', 'examples', 'plugins')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $folder) -Destination $packageDirectory -Recurse -Force
    }
    $referenceDirectory = Join-Path $packageDirectory 'api\csharp'
    $null = New-Item -ItemType Directory -Path $referenceDirectory -Force
    Copy-Item -LiteralPath (Join-Path $buildArtifacts 'bin\LightRcon.Core\release\LightRcon.Core.dll') -Destination $referenceDirectory -Force
}
Write-Host "Published desktop and console programs to $packageRoot."
