<#
.SYNOPSIS
    Runs all unit tests in the solution.

.DESCRIPTION
    This script runs all tests in the solution using dotnet test.
    Test results are saved to TestResults/trx folder in TRX format.

.PARAMETER Configuration
    The build configuration (Debug or Release). Default is 'Release'.

.PARAMETER NoBuild
    If specified, skips the build step (assumes solution is already built).

.PARAMETER Filter
    Optional test filter expression (e.g., "FullyQualifiedName~MyTest").

.EXAMPLE
    .\Test.ps1
    .\Test.ps1 -Configuration Debug
    .\Test.ps1 -NoBuild
    .\Test.ps1 -Filter "Category=Unit"
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [switch]$NoBuild,
    
    [string]$Filter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionFile = Get-ChildItem -Path $repoRoot -Filter '*.sln' -File | Select-Object -First 1

if (-not $solutionFile) {
    throw "No solution file found in $repoRoot"
}

$solutionPath = $solutionFile.FullName
$solutionName = $solutionFile.BaseName
$resultsDir = Join-Path $repoRoot 'TestResults'
$trxDir = Join-Path $resultsDir 'trx'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Testing: $solutionName" -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Push-Location $repoRoot
try {
    # Ensure test results directory exists
    if (-not (Test-Path $trxDir)) {
        New-Item -ItemType Directory -Path $trxDir -Force | Out-Null
    }

    # Build arguments
    $testArgs = @(
        'test',
        $solutionPath,
        '-c', $Configuration,
        '--results-directory', $trxDir,
        '--logger', 'trx;LogFileName=TestResults.trx'
    )

    if ($NoBuild) {
        $testArgs += '--no-build'
    }

    if ($Filter) {
        $testArgs += '--filter'
        $testArgs += $Filter
    }

    Write-Host "[Test] Running tests..." -ForegroundColor Yellow
    & dotnet @testArgs

    Write-Host ""
    Write-Host "[Test] Test results saved to: $trxDir" -ForegroundColor Green
}
finally {
    Pop-Location
}
