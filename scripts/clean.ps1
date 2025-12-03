param()
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Khaos.Processing.Pipelines.sln'
$pathsToClear = @(
    (Join-Path $repoRoot 'TestResults'),
    (Join-Path $repoRoot 'artifacts')
)

Push-Location $repoRoot
try {
    foreach ($path in $pathsToClear) {
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    dotnet clean $solution -c Release
}
finally {
    Pop-Location
}
