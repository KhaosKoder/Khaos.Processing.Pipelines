param()
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Khaos.Processing.Pipelines.sln'
$resultsDir = Join-Path $repoRoot 'TestResults'

if (-not (Test-Path $resultsDir)) {
    New-Item -ItemType Directory -Path $resultsDir | Out-Null
}

Push-Location $repoRoot
try {
    dotnet test $solution -c Release --results-directory $resultsDir --logger "trx;LogFileName=tests.trx"
}
finally {
    Pop-Location
}
