Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'C# compiler was not found.' }
$testDir = Join-Path $PSScriptRoot 'dist\tests'
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
$testExe = Join-Path $testDir 'RuntimeLifecycleTests.exe'
$sourceDirectories = @('Core', 'Runtime', 'Proxy') | ForEach-Object { Join-Path $PSScriptRoot "src\$_" }
$sources = @(Get-ChildItem -LiteralPath $sourceDirectories -Filter '*.cs' -Recurse -File | ForEach-Object { $_.FullName })
& $compiler /nologo /noconfig /langversion:5 /platform:x64 /codepage:65001 /target:exe /reference:System.dll /reference:System.Core.dll "/out:$testExe" @sources (Join-Path $PSScriptRoot 'tests\RuntimeLifecycleTests.cs')
if ($LASTEXITCODE -ne 0) { throw "Test compilation failed with exit code $LASTEXITCODE." }
& $testExe
if ($LASTEXITCODE -ne 0) { throw "Lifecycle tests failed with exit code $LASTEXITCODE." }
