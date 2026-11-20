param(
    [Parameter(Mandatory = $true)][string]$DataDirectory,
    [string]$ServerId,
    [string]$HostExecutable = (Join-Path $PSScriptRoot 'LightRcon.Host.exe')
)
$ErrorActionPreference = 'Stop'
$resolvedData = (Resolve-Path -LiteralPath $DataDirectory).ProviderPath
$resolvedHost = (Resolve-Path -LiteralPath $HostExecutable).ProviderPath
if (!(Test-Path -LiteralPath (Join-Path $resolvedData 'profiles.json') -PathType Leaf)) {
    throw "No profiles.json in $resolvedData. Select the existing data directory from the working installation."
}
$hostArguments = @('--data-dir', $resolvedData)
if ($ServerId) {
    $parsedId = [Guid]::Empty
    if (![Guid]::TryParse($ServerId, [ref]$parsedId)) { throw 'ServerId must be a saved profile GUID.' }
    $hostArguments += @('--server', $parsedId.ToString())
}
Write-Host "Starting existing profiles from: $resolvedData"
& $resolvedHost @hostArguments
exit $LASTEXITCODE
