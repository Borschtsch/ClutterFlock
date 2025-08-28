# Coverage Report Generation Script for ClutterFlock
# This script runs tests with coverage and generates comprehensive reports

param(
    [string]$Configuration = "Release",
    [switch]$OpenReport = $true,
    [switch]$SkipBuild = $false,
    [switch]$Verbose = $false
)

Write-Host "=== ClutterFlock Coverage Report Generator ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor White

# Step 1: Clean and build (unless skipped)
if (-not $SkipBuild) {
    Write-Host "`n1. Cleaning and building solution..." -ForegroundColor Yellow
    
    dotnet clean ClutterFlock.sln
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Clean failed"
        exit 1
    }
    
    dotnet build ClutterFlock.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
    
    Write-Host "✅ Build completed successfully" -ForegroundColor Green
} else {
    Write-Host "⏭️  Skipping build step" -ForegroundColor Yellow
}

# Step 2: Ensure TestResults directory exists
Write-Host "`n2. Preparing test environment..." -ForegroundColor Yellow
$testResultsDir = "ClutterFlock.Tests/TestResults"
if (-not (Test-Path $testResultsDir)) {
    New-Item -ItemType Directory -Path $testResultsDir -Force | Out-Null
    Write-Host "Created TestResults directory" -ForegroundColor Green
}

# Step 3: Run tests with coverage
Write-Host "`n3. Running tests with coverage collection..." -ForegroundColor Yellow
$testCommand = "dotnet test ClutterFlock.Tests/ClutterFlock.Tests.csproj -c $Configuration --no-build --verbosity normal /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=TestResults/coverage `"/p:Include=[ClutterFlock]*`""

Write-Host "Executing: $testCommand" -ForegroundColor Cyan
Invoke-Expression $testCommand

if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed"
    exit 1
}

Write-Host "✅ Tests completed successfully" -ForegroundColor Green

# Step 4: Generate HTML report
Write-Host "`n4. Generating HTML coverage report..." -ForegroundColor Yellow

$coverageFile = "ClutterFlock.Tests/TestResults/coverage.cobertura.xml"
if (-not (Test-Path $coverageFile)) {
    Write-Error "Coverage file not found: $coverageFile"
    exit 1
}

# Check if reportgenerator is available
try {
    reportgenerator --help | Out-Null
} catch {
    Write-Error "ReportGenerator not found. Please install it:"
    Write-Host "  dotnet tool install -g dotnet-reportgenerator-globaltool" -ForegroundColor Cyan
    exit 1
}

$reportDir = "coverage-report"
reportgenerator -reports:$coverageFile -targetdir:$reportDir -reporttypes:Html

if ($LASTEXITCODE -ne 0) {
    Write-Error "Report generation failed"
    exit 1
}

Write-Host "✅ HTML report generated in $reportDir" -ForegroundColor Green

# Step 5: Validate coverage thresholds
Write-Host "`n5. Validating coverage thresholds..." -ForegroundColor Yellow
$validateScript = "scripts/validate-coverage.ps1"
if (Test-Path $validateScript) {
    & $validateScript -CoverageFile $coverageFile -Verbose:$Verbose
    $validationResult = $LASTEXITCODE
} else {
    Write-Warning "Coverage validation script not found: $validateScript"
    $validationResult = 0
}

# Step 6: Open report if requested
if ($OpenReport -and (Test-Path "$reportDir/index.html")) {
    Write-Host "`n6. Opening coverage report..." -ForegroundColor Yellow
    Start-Process "$reportDir/index.html"
    Write-Host "✅ Coverage report opened in browser" -ForegroundColor Green
}

# Summary
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Coverage report available at: $reportDir/index.html" -ForegroundColor White
Write-Host "Coverage data file: $coverageFile" -ForegroundColor White

if ($validationResult -eq 0) {
    Write-Host "✅ Coverage validation passed" -ForegroundColor Green
} else {
    Write-Host "❌ Coverage validation failed" -ForegroundColor Red
}

Write-Host "`nTo run this script again:" -ForegroundColor Yellow
Write-Host "  .\scripts\coverage-report.ps1" -ForegroundColor Cyan
Write-Host "  .\scripts\coverage-report.ps1 -SkipBuild -Verbose" -ForegroundColor Cyan

exit $validationResult