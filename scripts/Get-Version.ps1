<#
.SYNOPSIS
    Displays the current version information for the solution.

.DESCRIPTION
    This script reads version information from MinVer based on Git tags.
    It shows the current version, the last release tag, and any pre-release suffix.

.EXAMPLE
    .\Get-Version.ps1
#>

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionFile = Get-ChildItem -Path $repoRoot -Filter '*.sln' -File | Select-Object -First 1

if (-not $solutionFile) {
    throw "No solution file found in $repoRoot"
}

$solutionName = $solutionFile.BaseName

# Read MinVerTagPrefix from Directory.Build.props
$propsFile = Join-Path $repoRoot 'Directory.Build.props'
if (-not (Test-Path $propsFile)) {
    throw "Directory.Build.props not found in $repoRoot"
}

$propsContent = [xml](Get-Content $propsFile -Raw)
$tagPrefix = $propsContent.Project.PropertyGroup | ForEach-Object { $_.MinVerTagPrefix } | Where-Object { $_ } | Select-Object -First 1

if (-not $tagPrefix) {
    # Try to derive from solution name
    $tagPrefix = "$solutionName/v"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Version Info: $solutionName" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Push-Location $repoRoot
try {
    # Get all tags matching the prefix
    $allTags = git tag -l "$tagPrefix*" 2>$null | Sort-Object -Descending
    
    if ($allTags) {
        $latestTag = $allTags | Select-Object -First 1
        Write-Host "[Version] Tag Prefix:      $tagPrefix" -ForegroundColor White
        Write-Host "[Version] Latest Tag:      $latestTag" -ForegroundColor Green
        
        # Extract version from tag
        $version = $latestTag -replace [regex]::Escape($tagPrefix), ''
        Write-Host "[Version] Latest Version:  $version" -ForegroundColor Green
        Write-Host ""
        
        # Show recent tags
        Write-Host "[Version] Recent releases:" -ForegroundColor Yellow
        $allTags | Select-Object -First 5 | ForEach-Object {
            $ver = $_ -replace [regex]::Escape($tagPrefix), ''
            Write-Host "  - $ver ($_)" -ForegroundColor DarkGray
        }
    }
    else {
        Write-Host "[Version] Tag Prefix:      $tagPrefix" -ForegroundColor White
        Write-Host "[Version] No release tags found." -ForegroundColor Yellow
        Write-Host "[Version] First release will be: ${tagPrefix}0.1.0" -ForegroundColor DarkGray
    }
    
    Write-Host ""
    
    # Get current commit info
    $currentCommit = git rev-parse --short HEAD 2>$null
    $isDirty = git status --porcelain 2>$null
    
    Write-Host "[Version] Current commit:  $currentCommit" -ForegroundColor White
    if ($isDirty) {
        Write-Host "[Version] Working tree:    DIRTY (uncommitted changes)" -ForegroundColor Red
    }
    else {
        Write-Host "[Version] Working tree:    Clean" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "To create a new release, use: .\Tag-Release.ps1 -Version X.Y.Z" -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
