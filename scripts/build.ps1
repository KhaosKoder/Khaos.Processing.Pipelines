param()
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Khaos.Processing.Pipelines.sln'

Push-Location $repoRoot
try {
    dotnet restore $solution
    dotnet build $solution -c Release
}
finally {
    Pop-Location
}
