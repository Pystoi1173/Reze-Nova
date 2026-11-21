param(
    [string]$ArchivePath = (Join-Path $PSScriptRoot 'vendor\zapret2-v1.0.7.zip')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'dist\runtime'
if (Test-Path -LiteralPath $destination) {
    throw "Runtime directory already exists. Move it aside before packaging to preserve custom lists: $destination"
}
$approved = @{
    '35E14E4E1F345B870C3B87269FA45353EFD597241A25304D38375C46B9FD2967' = $true
    '547A5DCE3F4820721A425680EA3F7F3971B7E9C772B8C795DD2D623148DA0928' = $true
}
$archive = (Get-Item -LiteralPath $ArchivePath).FullName
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if (-not $approved.ContainsKey($hash)) { throw 'Use the verified official zapret2 v1.0.7 ZIP or TAR.GZ release.' }

$files = [ordered]@{
    'binaries/windows-x86_64/winws2.exe' = 'bin\winws2.exe'
    'binaries/windows-x86_64/cygwin1.dll' = 'bin\cygwin1.dll'
    'binaries/windows-x86_64/WinDivert.dll' = 'bin\WinDivert.dll'
    'binaries/windows-x86_64/WinDivert64.sys' = 'bin\WinDivert64.sys'
    'lua/zapret-lib.lua' = 'lua\zapret-lib.lua'
    'lua/zapret-antidpi.lua' = 'lua\zapret-antidpi.lua'
    'files/fake/quic_initial_www_google_com.bin' = 'fake\quic_initial_www_google_com.bin'
    'files/fake/discord-ip-discovery-with-port.bin' = 'fake\discord-ip-discovery-with-port.bin'
    'files/fake/stun.bin' = 'fake\stun.bin'
    'init.d/windivert.filter.examples/windivert_part.discord_media.txt' = 'windivert\windivert_part.discord_media.txt'
    'init.d/windivert.filter.examples/windivert_part.stun.txt' = 'windivert\windivert_part.stun.txt'
    'init.d/windivert.filter.examples/windivert_part.wireguard.txt' = 'windivert\windivert_part.wireguard.txt'
    'init.d/windivert.filter.examples/windivert_part.quic_initial_ietf.txt' = 'windivert\windivert_part.quic_initial_ietf.txt'
    'docs/LICENSE.txt' = 'LICENSE-zapret2.txt'
}
$dist = Split-Path -Parent $destination
$stagingName = 'runtime-staging-' + [Guid]::NewGuid().ToString('N')
$staging = Join-Path $dist $stagingName
$extract = Join-Path $staging 'extract'
$runtime = Join-Path $staging 'runtime'
try {
    New-Item -ItemType Directory -Path $extract,$runtime -Force | Out-Null
    $archiveRoot = 'zapret2-v1.0.7'
    $entries = @($files.Keys | ForEach-Object { "$archiveRoot/$_" })
    & "$env:SystemRoot\System32\tar.exe" -xf $archive -C $extract -- @entries
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract the required release files.' }
    foreach ($file in $files.GetEnumerator()) {
        $target = Join-Path $runtime $file.Value
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $extract "$archiveRoot/$($file.Key)") -Destination $target
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\active-discord-udp.bin') -Destination (Join-Path $runtime 'fake\active-discord-udp.bin')
    $lists = Join-Path $runtime 'lists'
    New-Item -ItemType Directory -Path $lists | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\services.txt') -Destination $lists
    New-Item -ItemType File -Path (Join-Path $lists 'include-user.txt'),(Join-Path $lists 'exclude-user.txt'),(Join-Path $lists 'ipset-user.txt'),(Join-Path $lists 'game-ports.txt') | Out-Null

    $validator = & (Join-Path $PSScriptRoot 'build.ps1') -OutputPath "dist\$stagingName\Reze-Nova.exe" -Console
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $PSNativeCommandUseErrorActionPreference = $false
        $validation = & $validator self-test --runtime-dir $runtime 2>&1
        $validationExit = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedPreference
    }
    if ($validationExit -ne 0) { throw "The staged runtime was not published. $validation" }
    [IO.Directory]::Move($runtime, $destination)
    Write-Output $destination
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
