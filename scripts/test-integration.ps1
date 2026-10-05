param([switch]$NoBuild, [string]$Configuration = 'Release', [ValidateRange(1, 1800)][int]$TimeoutSeconds = 180)
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
    New-Item -ItemType Directory -Path $runResults -Force | Out-Null
    # The runner timeout is cooperative; a blocked WPF dispatcher can prevent shutdown.
    # Supervise only this child process tree with an independent wall-clock deadline.
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('test', '--project', 'ClutterFlock.Tests/ClutterFlock.Tests.csproj', '-c', $Configuration,
        '--no-build', '--coverlet', '--coverlet-output-format', 'cobertura', '--coverlet-include', '[ClutterFlock]*',
        '--results-directory', $runResults, '--report-trx', '--timeout', '2m', '--output', 'Detailed',
        '--diagnostic', '--diagnostic-verbosity', 'Information', '--diagnostic-output-directory', $runResults)) {
        $start.ArgumentList.Add($argument)
    }
    $testProcess = [System.Diagnostics.Process]::Start($start)
    $outputCopy = $testProcess.StandardOutput.BaseStream.CopyToAsync([Console]::OpenStandardOutput())
    $errorCopy = $testProcess.StandardError.BaseStream.CopyToAsync([Console]::OpenStandardError())
    Write-Host "Test process $($testProcess.Id); hard timeout ${TimeoutSeconds}s; diagnostics: $runResults"
    try {
        if (-not $testProcess.WaitForExit($TimeoutSeconds * 1000)) {
            $message = "Integration test process exceeded ${TimeoutSeconds}s. Terminating its process tree. Diagnostics: $runResults"
            [System.IO.File]::WriteAllText((Join-Path $runResults 'timeout.txt'), $message)
            Write-Host $message -ForegroundColor Red
            $testProcess.Kill($true)
            [void]$testProcess.WaitForExit(10000)
            exit 124
        }
        $testExit = $testProcess.ExitCode
        [void][System.Threading.Tasks.Task]::WhenAll($outputCopy, $errorCopy).WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
    }
    finally {
        if (-not $testProcess.HasExited) { $testProcess.Kill($true) }
        $testProcess.Dispose()
    }
    $reports = @(Get-ChildItem -LiteralPath $runResults -Filter 'coverage.cobertura*.xml')
    if ($reports.Count -ne 1) {
        if ($testExit -ne 0) { exit $testExit }
        throw 'Expected one coverage report from this integration run.'
    }
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
