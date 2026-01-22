<#
.SYNOPSIS
    Cleans the solution by removing all build artifacts, test results, and intermediate files.

.DESCRIPTION
    This script performs a comprehensive clean of the solution:
    1. Runs 'dotnet clean' on the solution
    2. Removes all bin and obj directories recursively
    3. Removes artifacts, TestResults, and .vs directories

.PARAMETER Configuration
    The build configuration to clean. Default is 'Release'.

.EXAMPLE
    .\Clean.ps1
    .\Clean.ps1 -Configuration Debug
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
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

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Cleaning: $solutionName" -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Push-Location $repoRoot
try {
    # Step 1: Run dotnet clean
    Write-Host "[Clean] Running dotnet clean..." -ForegroundColor Yellow
    dotnet clean $solutionPath -c $Configuration --verbosity minimal
    Write-Host ""

    # Step 2: Remove standard output directories
    $foldersToRemove = @(
        (Join-Path $repoRoot 'artifacts'),
        (Join-Path $repoRoot 'TestResults'),
        (Join-Path $repoRoot '.vs')
    )

    foreach ($folder in $foldersToRemove) {
        if (Test-Path $folder) {
            Write-Host "[Clean] Removing $folder" -ForegroundColor DarkGray
            Remove-Item -Path $folder -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # Step 3: Recursively remove bin and obj directories
    Write-Host "[Clean] Removing bin and obj directories..." -ForegroundColor Yellow
    Get-ChildItem -Path $repoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        ForEach-Object {
            Write-Host "[Clean] Removing $($_.FullName)" -ForegroundColor DarkGray
            Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }

    Write-Host ""
    Write-Host "[Clean] Clean completed successfully." -ForegroundColor Green
}
finally {
    Pop-Location
}
