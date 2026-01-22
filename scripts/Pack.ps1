<#
.SYNOPSIS
    Creates NuGet packages for the solution.

.DESCRIPTION
    Builds and packs the Khaos.Processing.Pipelines solution into NuGet packages.

.PARAMETER Configuration
    Build configuration. Default is 'Release'.

.PARAMETER NoBuild
    Skip building and pack existing binaries.

.PARAMETER OutputDirectory
    Custom output directory for packages. Default is 'artifacts/packages'.

.EXAMPLE
    .\Pack.ps1
    .\Pack.ps1 -Configuration Debug
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$NoBuild,

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\packages'
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

Write-Host ""
Write-Host "Packing Khaos.Processing.Pipelines to $OutputDirectory" -ForegroundColor Cyan
Write-Host ""

Push-Location $repoRoot
try {
    $packArgs = @(
        'pack'
        '--configuration', $Configuration
        '--output', $OutputDirectory
        '--nologo'
    )

    if ($NoBuild) {
        $packArgs += '--no-build'
    }

    dotnet @packArgs Khaos.Processing.Pipelines.sln

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "Packages created in: $OutputDirectory" -ForegroundColor Green
}
finally {
    Pop-Location
}
