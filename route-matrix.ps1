param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'dist\logs\route-matrix-results.json'),
    [int]$ConnectTimeoutSeconds = 4,
    [int]$RequestTimeoutSeconds = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$nova = Join-Path $projectRoot 'dist\Reze-Nova.exe'
$logs = Join-Path $projectRoot 'dist\logs'
$curl = Join-Path $env:SystemRoot 'System32\curl.exe'
$targets = @('https://discord.com/','https://www.youtube.com/','https://telegram.org/')
$modes = @('auto','fast','stable','wide')

if (-not (Test-Path -LiteralPath $nova -PathType Leaf)) { throw 'Nova executable was not found.' }
if (-not (Test-Path -LiteralPath $curl -PathType Leaf)) { throw 'Windows curl.exe was not found.' }
New-Item -ItemType Directory -Path $logs -Force | Out-Null

function Invoke-HeadProbe([string]$target) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $curl
    $info.Arguments = '-q --noproxy * --proto =https --http1.1 --head --silent --show-error --max-time ' + $RequestTimeoutSeconds + ' --connect-timeout ' + $ConnectTimeoutSeconds + ' --write-out "HTTP %{http_code} / total %{time_total}s / connect %{time_connect}s / start %{time_starttransfer}s / peer %{remote_ip}" "' + $target + '"'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $result = [pscustomobject]@{ ExitCode = $process.ExitCode; Result = ($stdout + ' ' + $stderr).Trim() }
    $process.Dispose()
    return $result
}

function Stop-Tree([int]$id) {
    if ($id -gt 0) {
        & taskkill.exe /PID $id /T /F 2>$null | Out-Null
        Start-Sleep -Milliseconds 700
    }
}

function Find-ReadyLog([datetime]$startedAt) {
    $files = @(Get-ChildItem -LiteralPath $logs -Filter 'runtime-*.log' -File |
        Where-Object { $_.LastWriteTime -ge $startedAt.AddSeconds(-1) } |
        Sort-Object LastWriteTime -Descending)
    foreach ($file in $files) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($content.Contains('windivert initialized. capture is started.') -and $content.Contains('REZE_NOVA_LUA_READY')) {
            return $file.FullName
        }
    }
    return $null
}

$active = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('Reze-Nova.exe','winws.exe','winws2.exe') })
if ($active.Count -gt 0) { throw 'Stop active Nova or winws processes before starting the route matrix.' }

$results = New-Object Collections.Generic.List[object]
foreach ($target in $targets) {
    $probe = Invoke-HeadProbe $target
    $results.Add([pscustomobject]@{ Mode = 'baseline'; Ready = $false; RuntimeLog = ''; Target = $target; ExitCode = $probe.ExitCode; Result = $probe.Result })
}

foreach ($mode in $modes) {
    $startedAt = Get-Date
    $route = $null
    $runtimeLog = ''
    $ready = $false
    try {
        $route = Start-Process -FilePath $nova -ArgumentList @('start', $mode) -PassThru -WindowStyle Hidden
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while (!$route.HasExited -and $timer.Elapsed.TotalSeconds -lt 15) {
            $runtimeLog = Find-ReadyLog $startedAt
            if ($runtimeLog) { $ready = $true; break }
            Start-Sleep -Milliseconds 250
            $route.Refresh()
        }
        foreach ($target in $targets) {
            $probe = Invoke-HeadProbe $target
            $results.Add([pscustomobject]@{ Mode = $mode; Ready = $ready; RuntimeLog = $runtimeLog; Target = $target; ExitCode = $probe.ExitCode; Result = $probe.Result })
        }
    }
    finally {
        if ($route) {
            $route.Refresh()
            if (!$route.HasExited) { Stop-Tree $route.Id }
        }
    }
    Start-Sleep -Seconds 1
}

$fullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $fullPath) -Force | Out-Null
$payloadResults = @($results.ToArray())
$payload = [pscustomobject]@{
    GeneratedAt = (Get-Date).ToUniversalTime().ToString('o')
    ConnectTimeoutSeconds = $ConnectTimeoutSeconds
    RequestTimeoutSeconds = $RequestTimeoutSeconds
    Method = 'HEAD'
    Results = $payloadResults
}
$payload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fullPath -Encoding UTF8
$payload | ConvertTo-Json -Depth 5
