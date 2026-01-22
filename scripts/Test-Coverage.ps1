<#
.SYNOPSIS
    Runs tests with code coverage and generates HTML reports.

.DESCRIPTION
    This script runs all tests in the solution with code coverage collection,
    then generates HTML and Cobertura coverage reports using ReportGenerator.

    Output locations:
    - Raw coverage data: TestResults/coverage/
    - HTML report: TestResults/coverage-report/
    - Cobertura XML: TestResults/coverage-report/Cobertura.xml

.PARAMETER Configuration
    The build configuration (Debug or Release). Default is 'Release'.

.PARAMETER NoBuild
    If specified, skips the build step (assumes solution is already built).

.PARAMETER Open
    If specified, opens the HTML report in the default browser after generation.

.EXAMPLE
    .\Test-Coverage.ps1
    .\Test-Coverage.ps1 -Open
    .\Test-Coverage.ps1 -Configuration Debug -NoBuild
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [switch]$NoBuild,
    
    [switch]$Open
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
$resultsRoot = Join-Path $repoRoot 'TestResults'
$coverageDir = Join-Path $resultsRoot 'coverage'
$reportDir = Join-Path $resultsRoot 'coverage-report'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Test Coverage: $solutionName" -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Push-Location $repoRoot
try {
    # Step 1: Clean previous coverage artifacts
    Write-Host "[Coverage] Cleaning previous coverage data..." -ForegroundColor Yellow
    if (Test-Path $coverageDir) { Remove-Item $coverageDir -Recurse -Force }
    if (Test-Path $reportDir) { Remove-Item $reportDir -Recurse -Force }
    
    New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    Write-Host ""

    # Step 2: Run tests with coverage
    Write-Host "[Coverage] Running tests with coverage collection..." -ForegroundColor Yellow
    $testArgs = @(
        'test',
        $solutionPath,
        '-c', $Configuration,
        '--collect:XPlat Code Coverage',
        '--results-directory', $coverageDir
    )

    if ($NoBuild) {
        $testArgs += '--no-build'
    }

    & dotnet @testArgs
    Write-Host ""

    # Step 3: Find coverage files
    $coverageFiles = Get-ChildItem -Path $coverageDir -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue

    if (-not $coverageFiles -or $coverageFiles.Count -eq 0) {
        Write-Warning "No coverage files were generated. Ensure your test projects have the correct coverage packages."
        return
    }

    $coverageFilesList = ($coverageFiles | Select-Object -ExpandProperty FullName) -join ';'
    Write-Host "[Coverage] Found $($coverageFiles.Count) coverage file(s)" -ForegroundColor Green
    Write-Host ""

    # Step 4: Restore tools and generate report
    Write-Host "[Coverage] Restoring dotnet tools..." -ForegroundColor Yellow
    dotnet tool restore
    Write-Host ""

    Write-Host "[Coverage] Generating HTML report..." -ForegroundColor Yellow
    $reportArgs = @(
        "-reports:$coverageFilesList",
        "-targetdir:$reportDir",
        "-reporttypes:Html;Cobertura;TextSummary"
    )

    dotnet tool run reportgenerator @reportArgs
    Write-Host ""

    # Step 5: Display summary
    $summaryFile = Join-Path $reportDir 'Summary.txt'
    if (Test-Path $summaryFile) {
        Write-Host "[Coverage] Summary:" -ForegroundColor Cyan
        Write-Host "----------------------------------------" -ForegroundColor DarkGray
        Get-Content $summaryFile | Write-Host
        Write-Host "----------------------------------------" -ForegroundColor DarkGray
        Write-Host ""
    }

    Write-Host "[Coverage] Reports generated successfully:" -ForegroundColor Green
    Write-Host "  HTML Report: $reportDir\index.html" -ForegroundColor White
    Write-Host "  Cobertura:   $reportDir\Cobertura.xml" -ForegroundColor White
    Write-Host ""

    # Step 6: Optionally open report
    if ($Open) {
        $indexPath = Join-Path $reportDir 'index.html'
        if (Test-Path $indexPath) {
            Write-Host "[Coverage] Opening report in browser..." -ForegroundColor Yellow
            Start-Process $indexPath
        }
        else {
            Write-Warning "Report index not found at $indexPath"
        }
    }
}
finally {
    Pop-Location
}
