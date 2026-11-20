param(
    [switch]$SelfContained,
    [switch]$FrameworkDependent,
    [string]$OutputDirectory,
    [string]$PluginDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildArtifacts = Join-Path $projectRoot 'artifacts\build'
$packageRoot = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $projectRoot + ' release' } else { [IO.Path]::GetFullPath($OutputDirectory, $projectRoot) }
$pluginRoot = if ([string]::IsNullOrWhiteSpace($PluginDirectory)) { Join-Path $projectRoot 'plugins' } else { [IO.Path]::GetFullPath($PluginDirectory, $projectRoot) }
if (!(Test-Path -LiteralPath $pluginRoot -PathType Container)) { throw "Plugin bundle not found: $pluginRoot" }
foreach ($required in @('BF3', 'BF4', 'BFH', 'README.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $pluginRoot $required))) { throw "Missing plugin bundle item: $required" }
}
$null = New-Item -ItemType Directory -Path $packageRoot -Force
$staging = Join-Path $projectRoot ('artifacts\package-staging\' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $staging -Force
$noticeRoot = Join-Path $staging '.documents'
$null = New-Item -ItemType Directory -Path $noticeRoot
$notices = @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'CONTRIBUTING.md', 'PRoCon-COMPATIBILITY.md')
foreach ($notice in $notices) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination $noticeRoot
}
$variants = if ($SelfContained -and !$FrameworkDependent) { @($true) } elseif ($FrameworkDependent -and !$SelfContained) { @($false) } else { @($false, $true) }

function Copy-PackageTree([string]$Source, [string]$Destination, [string[]]$ExcludedPluginFamilies = @()) {
    $null = New-Item -ItemType Directory -Path $Destination -Force
    foreach ($file in Get-ChildItem -LiteralPath $Source -File -Recurse -Force) {
        if ($file.Name -eq '.gitkeep') { continue }
        if ($ExcludedPluginFamilies | Where-Object { $file.BaseName -eq $_ -or $file.Name.StartsWith($_ + '.', [StringComparison]::OrdinalIgnoreCase) }) { continue }
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
        if ($relative -match '(?i)(^|[\\/])(profiles\.json[^\\/]*|servers|\.git|\.local|artifacts)([\\/]|$)|\.(dpapi|pfx|p12|log)$') {
            throw "Local data cannot be packaged: $($file.FullName)"
        }
        $destinationFile = Join-Path $Destination $relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destinationFile) -Force
        Copy-Item -LiteralPath $file.FullName -Destination $destinationFile -Force
    }
}

foreach ($includeRuntime in $variants) {
    $mode = if ($includeRuntime) { 'true' } else { 'false' }
    $suffix = if ($includeRuntime) { ' (with dotnet)' } else { '' }
    foreach ($target in @('Desktop', 'Host')) {
        $name = "LightRconPlus-$target-x86_64_win$suffix"
        $destination = Join-Path $staging $name
        & dotnet publish (Join-Path $projectRoot "src\LightRcon.$target") --artifacts-path $buildArtifacts -c Release -r win-x64 --self-contained $mode -o $destination -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
        if ($LASTEXITCODE -ne 0) { throw "Publishing $name failed." }

        foreach ($notice in $notices) {
            Copy-Item -LiteralPath (Join-Path $noticeRoot $notice) -Destination (Join-Path $destination $notice)
        }
        $packageDocs = Join-Path $destination 'docs'
        $null = New-Item -ItemType Directory -Path $packageDocs -Force
        foreach ($document in @('VALIDATION.md', 'PLUGINS.md', 'LUA-API.md', 'GAME-SETTINGS.md')) {
            Copy-Item -LiteralPath (Join-Path $projectRoot "docs\$document") -Destination $packageDocs
        }
        foreach ($folder in @('licenses', 'gitbook')) {
            Copy-PackageTree (Join-Path $projectRoot "docs\$folder") (Join-Path $packageDocs $folder)
        }
        foreach ($folder in @('api', 'examples', 'plugins', 'Pictures')) {
            Copy-PackageTree (Join-Path $projectRoot $folder) (Join-Path $destination $folder)
        }
        # These three GPL-3.0-or-later plugins and their Battlelog helper are
        # intentionally excluded from releases; keep the original bundle intact.
        Copy-PackageTree $pluginRoot (Join-Path $destination 'plugins') @('CBasicInGameInfo', 'CInGameAdmin', 'CSpambot', 'CBattlelogCache')
        # Keep the game library directories even when every bundled plugin is excluded.
        foreach ($game in @('BF3', 'BF4', 'BFH')) {
            $null = New-Item -ItemType Directory -Path (Join-Path $destination "plugins\$game") -Force
        }
        $referenceDirectory = Join-Path $destination 'api\csharp'
        $null = New-Item -ItemType Directory -Path $referenceDirectory -Force
        Copy-Item -LiteralPath (Join-Path $buildArtifacts 'bin\LightRcon.Core\release\LightRcon.Core.dll') -Destination $referenceDirectory -Force
        if ($target -eq 'Host') {
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'start-existing-data.ps1') -Destination $destination
        }
        $archive = Join-Path $staging ($name + '.zip')
        [IO.Compression.ZipFile]::CreateFromDirectory($destination, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    }
}

# Publish only after all requested variants have been built successfully.
foreach ($directory in Get-ChildItem -LiteralPath $staging -Directory -Filter 'LightRconPlus-*') {
    $destination = Join-Path $packageRoot $directory.Name
    if (Test-Path -LiteralPath $destination) {
        $backup = Join-Path $projectRoot ('artifacts\release-backups\' + [Guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $backup -Force
        Move-Item -LiteralPath $destination -Destination $backup
    }
    Move-Item -LiteralPath $directory.FullName -Destination $destination
    [IO.File]::Move((Join-Path $staging ($directory.Name + '.zip')), (Join-Path $packageRoot ($directory.Name + '.zip')), $true)
}
Write-Host "Published packages with licensed bundled plugins excluded to $packageRoot."
