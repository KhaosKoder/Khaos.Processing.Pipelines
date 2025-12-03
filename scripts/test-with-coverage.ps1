param()
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Khaos.Processing.Pipelines.sln'
$resultsDir = Join-Path $repoRoot 'TestResults'
$coverageDir = Join-Path $resultsDir 'coverage'
$coveragePrefix = Join-Path $coverageDir 'coverage'
$reportDir = Join-Path $resultsDir 'coverage-report'
$coverageReport = "$coveragePrefix.cobertura.xml"

New-Item -ItemType Directory -Force -Path $coverageDir | Out-Null
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null

Push-Location $repoRoot
try {
    dotnet tool restore

    dotnet test $solution `
        -c Release `
        --results-directory $resultsDir `
        --logger "trx;LogFileName=coverage-tests.trx" `
        "/p:CollectCoverage=true" `
        "/p:CoverletOutput=$coveragePrefix" `
        "/p:CoverletOutputFormat=cobertura" `
        "/p:Threshold=70" `
        "/p:ThresholdType=line"

    dotnet tool run reportgenerator `
        "-reports:$coverageReport" `
        "-targetdir:$reportDir" `
        "-reporttypes:HtmlInline_AzurePipelines;Cobertura"
}
finally {
    Pop-Location
}
