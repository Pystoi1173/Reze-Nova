param(
    [string]$OutputPath,
    [switch]$Ui,
    [switch]$Console
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Console) { $Ui = $true }
if ([string]::IsNullOrEmpty($OutputPath)) { $OutputPath = if ($Ui) { 'dist\Reze-Nova.exe' } else { 'dist\Reze-Nova-core.exe' } }
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) { throw 'C# compiler was not found.' }

$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path $root $OutputPath))
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
$sourceDirectories = @('Core', 'Runtime', 'Proxy') | ForEach-Object { Join-Path $root "src\$_" }
if ($Ui) { $sourceDirectories += Join-Path $root 'src\UI' }
$sourceDirectories += Join-Path $root 'src'
$sources = @(Get-ChildItem -LiteralPath $sourceDirectories -Filter '*.cs' -File | Sort-Object FullName -Unique | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'No C# sources found.' }
$args = @('/nologo','/noconfig','/langversion:5','/platform:x64','/codepage:65001','/optimize+','/reference:System.dll','/reference:System.Core.dll',"/out:$output")
if ($Ui) {
    $framework = Split-Path -Parent $csc
    $args += @('/target:winexe', '/define:NOVA_UI')
    foreach ($assembly in @('WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'System.Xaml.dll')) {
        $reference = Join-Path $framework $assembly
        if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { throw "WPF assembly not found: $reference" }
        $args += "/reference:$reference"
    }
    foreach ($assembly in @('System.Windows.Forms.dll', 'System.Drawing.dll')) {
        $reference = Join-Path $framework $assembly
        if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { throw "UI assembly not found: $reference" }
        $args += "/reference:$reference"
    }
} else {
    $args += '/target:exe'
}
& $csc @args @sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }
if ($Ui) {
    $assetSource = Join-Path $root 'assets\ui'
    $assetDestination = Join-Path (Split-Path -Parent $output) 'assets\ui'
    if (Test-Path -LiteralPath $assetSource) {
        New-Item -ItemType Directory -Path $assetDestination -Force | Out-Null
        Get-ChildItem -LiteralPath $assetSource -File | Copy-Item -Destination $assetDestination -Force
    }
    $serviceList = Join-Path $root 'assets\services.txt'
    $runtimeLists = Join-Path (Split-Path -Parent $output) 'runtime\lists'
    if (Test-Path -LiteralPath $serviceList) {
        New-Item -ItemType Directory -Path $runtimeLists -Force | Out-Null
        Copy-Item -LiteralPath $serviceList -Destination (Join-Path $runtimeLists 'services.txt') -Force
    }
    New-Item -ItemType Directory -Path $runtimeLists -Force | Out-Null
    foreach ($listName in @('include-user.txt', 'exclude-user.txt', 'ipset-user.txt', 'game-ports.txt')) {
        $listPath = Join-Path $runtimeLists $listName
        if (-not (Test-Path -LiteralPath $listPath)) { New-Item -ItemType File -Path $listPath | Out-Null }
    }
}
Write-Output $output
