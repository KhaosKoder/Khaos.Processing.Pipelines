<#
.SYNOPSIS
    Builds the solution.

.DESCRIPTION
    This script performs a clean build of the solution:
    1. Optionally cleans the solution first
    2. Restores NuGet packages
    3. Builds the solution

.PARAMETER Configuration
    The build configuration (Debug or Release). Default is 'Release'.

.PARAMETER Clean
    If specified, runs Clean.ps1 before building.

.PARAMETER NoBuild
    If specified, only restores packages without building.

.EXAMPLE
    .\Build.ps1
    .\Build.ps1 -Configuration Debug
    .\Build.ps1 -Clean
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [switch]$Clean,
    
    [switch]$NoBuild
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
Write-Host "  Building: $solutionName" -ForegroundColor Cyan
Write-Host "  Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Optionally clean first
if ($Clean) {
    Write-Host "[Build] Running clean..." -ForegroundColor Yellow
    & "$PSScriptRoot\Clean.ps1" -Configuration $Configuration
    Write-Host ""
}

Push-Location $repoRoot
try {
    # Step 1: Restore packages
    Write-Host "[Build] Restoring NuGet packages..." -ForegroundColor Yellow
    dotnet restore $solutionPath
    Write-Host ""

    if (-not $NoBuild) {
        # Step 2: Build solution
        Write-Host "[Build] Building solution..." -ForegroundColor Yellow
        dotnet build $solutionPath -c $Configuration --no-restore
        Write-Host ""
    }

    Write-Host "[Build] Build completed successfully." -ForegroundColor Green
}
finally {
    Pop-Location
}
