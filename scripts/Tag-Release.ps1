<#
.SYNOPSIS
    Creates a new release tag for the solution.

.DESCRIPTION
    This script creates and pushes a Git tag following the MinVer tag pattern.
    The tag format is: {ProductName}/vX.Y.Z (e.g., Khaos.Processing.Pipelines/v1.2.0)

.PARAMETER Version
    The semantic version to tag (e.g., "1.2.0", "2.0.0-beta.1").

.PARAMETER Push
    If specified, pushes the tag to the remote repository.

.PARAMETER Force
    If specified, overwrites an existing tag with the same version.

.EXAMPLE
    .\Tag-Release.ps1 -Version 1.0.0
    .\Tag-Release.ps1 -Version 1.0.0 -Push
    .\Tag-Release.ps1 -Version 1.0.0 -Push -Force
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$')]
    [string]$Version,
    
    [switch]$Push,
    
    [switch]$Force
)

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
    $tagPrefix = "$solutionName/v"
}

$tagName = "$tagPrefix$Version"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Tag Release: $solutionName" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Push-Location $repoRoot
try {
    # Check for uncommitted changes
    $isDirty = git status --porcelain 2>$null
    if ($isDirty) {
        Write-Warning "Working tree has uncommitted changes. Commit or stash them before tagging."
        Write-Host ""
        git status --short
        Write-Host ""
        
        $response = Read-Host "Continue anyway? (y/N)"
        if ($response -ne 'y' -and $response -ne 'Y') {
            Write-Host "Aborted." -ForegroundColor Yellow
            return
        }
    }

    # Check if tag exists
    $existingTag = git tag -l $tagName 2>$null
    if ($existingTag) {
        if ($Force) {
            Write-Host "[Tag] Removing existing tag: $tagName" -ForegroundColor Yellow
            git tag -d $tagName 2>$null
            if ($Push) {
                git push origin --delete $tagName 2>$null
            }
        }
        else {
            throw "Tag '$tagName' already exists. Use -Force to overwrite."
        }
    }

    # Create the tag
    Write-Host "[Tag] Creating tag: $tagName" -ForegroundColor Yellow
    
    if ($PSCmdlet.ShouldProcess($tagName, "Create Git tag")) {
        git tag -a $tagName -m "Release $Version"
        
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create tag"
        }
        
        Write-Host "[Tag] Tag created successfully." -ForegroundColor Green
        Write-Host ""

        # Push if requested
        if ($Push) {
            Write-Host "[Tag] Pushing tag to origin..." -ForegroundColor Yellow
            git push origin $tagName
            
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to push tag"
            }
            
            Write-Host "[Tag] Tag pushed successfully." -ForegroundColor Green
        }
        else {
            Write-Host "[Tag] To push this tag, run:" -ForegroundColor DarkGray
            Write-Host "      git push origin $tagName" -ForegroundColor White
        }
    }

    Write-Host ""
    Write-Host "[Tag] Release Summary:" -ForegroundColor Cyan
    Write-Host "  Solution: $solutionName" -ForegroundColor White
    Write-Host "  Version:  $Version" -ForegroundColor White
    Write-Host "  Tag:      $tagName" -ForegroundColor White
    Write-Host ""
    Write-Host "[Tag] Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Build and pack: .\Pack.ps1" -ForegroundColor DarkGray
    Write-Host "  2. Verify package version matches: $Version" -ForegroundColor DarkGray
    Write-Host "  3. Publish to NuGet feed" -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
