param([switch]$NoBuild, [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if (-not $NoBuild) {
        dotnet build ClutterFlock.Tests/ClutterFlock.Tests.csproj -c $Configuration
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $results = Join-Path $repoRoot 'ClutterFlock.Tests/TestResults'
    $runResults = Join-Path $results ([Guid]::NewGuid().ToString('N'))
    dotnet test --project ClutterFlock.Tests/ClutterFlock.Tests.csproj -c $Configuration --no-build --coverlet --coverlet-output-format cobertura --coverlet-include '[ClutterFlock]*' --results-directory $runResults --report-trx --timeout 2m
    $testExit = $LASTEXITCODE
    $reports = @(Get-ChildItem -LiteralPath $runResults -Filter 'coverage.cobertura*.xml')
    if ($reports.Count -ne 1) { throw 'Expected one coverage report from this integration run.' }
    Copy-Item -LiteralPath $reports[0].FullName -Destination (Join-Path $results 'coverage.cobertura.xml') -Force
    Get-ChildItem -LiteralPath $runResults -Filter '*.trx' | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $results -Force
    }
    [xml]$coverage = Get-Content -LiteralPath $reports[0].FullName
    $lineRate = [double]$coverage.coverage.'line-rate' * 100
    $branchRate = [double]$coverage.coverage.'branch-rate' * 100
    Write-Host ('Integration coverage: {0:F2}% lines, {1:F2}% branches' -f $lineRate, $branchRate)
    if ($testExit -ne 0) { exit $testExit }
    # Set minimum coverage threshold (adjust as needed).
    # Low coverage now fails the run directly; the former optional exit is no longer needed.
    # Preserve the repository's existing integration coverage gate.
    if ($lineRate -lt 75 -or $branchRate -lt 60) { throw 'Coverage is below 75% lines / 60% branches.' }
}
finally { Pop-Location }
