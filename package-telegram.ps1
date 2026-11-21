param(
    [string]$OutputPath = 'dist\telegram-proxy',
    [Alias('AssetPath')][string]$SourcePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$expectedHash = '454205cbcf76f333934108d893b58a528373bce8096624e27f0b6dc16eea660f'
$releaseUrl = 'https://github.com/Flowseal/tg-ws-proxy/releases/download/v1.11.4/TgWsProxy_windows.exe'
$license = Join-Path $PSScriptRoot 'vendor\flowseal-tg-ws-proxy-1.11.4\LICENSE.txt'
if (-not [IO.Path]::IsPathRooted($OutputPath)) { $OutputPath = Join-Path $PSScriptRoot $OutputPath }
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)

if (-not $SourcePath -and $env:LOCALAPPDATA) {
    $cached = Join-Path $env:LOCALAPPDATA 'Temp\opencode\TgWsProxy-v1.11.4.exe'
    if (Test-Path -LiteralPath $cached -PathType Leaf) { $SourcePath = $cached }
}
if ($SourcePath -and -not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw 'The supplied Flowseal release asset was not found.'
}
if (-not (Test-Path -LiteralPath $license -PathType Leaf)) { throw 'The Flowseal license notice was not found.' }

[void][IO.Directory]::CreateDirectory($destination)
$staged = Join-Path $destination ('.tgws-' + [Guid]::NewGuid().ToString('N') + '.tmp')
$backup = $staged + '.bak'
$engine = Join-Path $destination 'TgWsProxy.exe'
try {
    if ($SourcePath) {
        [IO.File]::Copy((Resolve-Path -LiteralPath $SourcePath).ProviderPath, $staged)
    } else {
        Invoke-WebRequest -Uri $releaseUrl -UseBasicParsing -OutFile $staged
    }
    if ((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'The Flowseal tg-ws-proxy v1.11.4 Windows x64 SHA-256 did not match.'
    }
    [IO.File]::Copy($license, (Join-Path $destination 'LICENSE.txt'), $true)
    if ([IO.File]::Exists($engine)) {
        [IO.File]::Replace($staged, $engine, $backup)
        [IO.File]::Delete($backup)
    } else {
        [IO.File]::Move($staged, $engine)
    }
} finally {
    if ([IO.File]::Exists($staged)) { [IO.File]::Delete($staged) }
}
Write-Output $destination
