param(
    [switch]$Probe,
    [string]$NovaPath = (Join-Path $PSScriptRoot 'dist\Reze-Nova.exe')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DiagnosticCommand {
    param([string]$Path, [string[]]$Arguments)

    # PowerShell 5.1 reports native stderr as an error record even for diagnostics.
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    $lines = & $Path @Arguments 2>&1 | ForEach-Object { $_.ToString() }
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = $lines }
}

$ready = $true
$nova = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($NovaPath)
$runtime = Join-Path (Split-Path -Parent $nova) 'runtime'
$hasNova = Test-Path -LiteralPath $nova -PathType Leaf
$hasRuntime = Test-Path -LiteralPath $runtime -PathType Container
if (-not $hasNova) {
    Write-Output "BLOCKED: Nova executable missing at '$nova' (build.ps1 output)."
    $ready = $false
}
if (-not $hasRuntime) {
    Write-Output "BLOCKED: Runtime directory missing at '$runtime' (package-runtime.ps1 output)."
    $ready = $false
}
if ($hasNova -and $hasRuntime) {
    try {
        $manifest = Invoke-DiagnosticCommand -Path $nova -Arguments 'self-test'
        if ($manifest.ExitCode -eq 0) {
            Write-Output 'OK: Runtime manifest and four profiles validated by self-test.'
        } else {
            Write-Output "BLOCKED: Nova self-test exited with code $($manifest.ExitCode)."
            Write-Output $manifest.Lines
            $ready = $false
        }
    } catch {
        Write-Output "BLOCKED: Could not run Nova self-test. $($_.Exception.Message)"
        $ready = $false
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Output 'OK: Elevated console.'
    } else {
        Write-Output 'BLOCKED: Starting the system route requires an elevated console.'
        $ready = $false
    }
} finally {
    $identity.Dispose()
}

$engines = @(Get-Process | Where-Object { $_.ProcessName -in 'winws', 'winws2' })
if ($engines.Count -eq 0) {
    Write-Output 'OK: No winws or winws2 processes found.'
} else {
    foreach ($engine in $engines) {
        try { $enginePath = $engine.Path } catch { $enginePath = $null }
        if (-not $enginePath) { $enginePath = '<path unavailable>' }
        Write-Output ('BLOCKED: Starting a new route with {0}.exe active (PID {1}, {2}).' -f $engine.ProcessName, $engine.Id, $enginePath)
    }
    $ready = $false
}

if ($Probe) {
    Write-Output 'HTTPS observations for the current route (curl config and explicit proxies bypassed).'
    Write-Output 'Use the README comparison procedure to attribute changes to Nova.'
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (-not (Test-Path -LiteralPath $curl -PathType Leaf)) {
        Write-Output 'HTTPS checks skipped: Windows curl.exe was not found.'
    } else {
        $targets = @('https://discord.com/', 'https://www.youtube.com/', 'https://telegram.org/')
        foreach ($target in $targets) {
            $curlArgs = @(
                '-q', '--noproxy', '*', '--proto', '=https', '--http1.1', '--head',
                '--silent', '--show-error', '--max-time', '8', '--connect-timeout', '4',
                '--write-out', 'HTTP %{http_code} / %{time_total}s / peer %{remote_ip}', $target
            )
            try {
                $result = Invoke-DiagnosticCommand -Path $curl -Arguments $curlArgs
                Write-Output ('{0} (curl exit {1})' -f $target, $result.ExitCode)
                Write-Output $result.Lines
            } catch {
                Write-Output ('{0} (curl could not run) {1}' -f $target, $_.Exception.Message)
            }
        }
    }
}

if (-not $ready) { exit 1 }
exit 0
