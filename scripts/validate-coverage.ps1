# Coverage Validation Script for ClutterFlock
# This script validates that code coverage meets minimum thresholds

param(
    [string]$CoverageFile = "ClutterFlock.Tests/TestResults/coverage.cobertura.xml",
    [string]$ConfigFile = "coverage-config.json",
    [switch]$FailOnThreshold = $true,
    [switch]$Verbose = $false
)

# Load configuration
if (Test-Path $ConfigFile) {
    $config = Get-Content $ConfigFile | ConvertFrom-Json
    Write-Host "Loaded coverage configuration from $ConfigFile" -ForegroundColor Green
} else {
    Write-Warning "Coverage configuration file not found: $ConfigFile"
    Write-Host "Using default thresholds: Lines 100%, Branches 100%" -ForegroundColor Yellow
    $config = @{
        coverageThresholds = @{
            global = @{
                lines = 100
                branches = 100
            }
        }
    }
}

# Check if coverage file exists
if (-not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found: $CoverageFile"
    Write-Host "Please run tests with coverage first:" -ForegroundColor Yellow
    Write-Host "  dotnet test ClutterFlock.Tests/ClutterFlock.Tests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=ClutterFlock.Tests/TestResults/coverage /p:Include=`"[ClutterFlock]*`"" -ForegroundColor Cyan
    exit 1
}

# Parse coverage XML
try {
    [xml]$coverageXml = Get-Content $CoverageFile
    $coverage = $coverageXml.coverage
    
    $lineRate = [math]::Round([double]$coverage.'line-rate' * 100, 2)
    $branchRate = [math]::Round([double]$coverage.'branch-rate' * 100, 2)
    $linesCovered = [int]$coverage.'lines-covered'
    $linesValid = [int]$coverage.'lines-valid'
    $branchesCovered = [int]$coverage.'branches-covered'
    $branchesValid = [int]$coverage.'branches-valid'
    
    Write-Host "`n=== Coverage Report ===" -ForegroundColor Cyan
    Write-Host "Line Coverage:   $lineRate% ($linesCovered/$linesValid lines)" -ForegroundColor White
    Write-Host "Branch Coverage: $branchRate% ($branchesCovered/$branchesValid branches)" -ForegroundColor White
    
    # Check thresholds
    $thresholds = $config.coverageThresholds.global
    $lineThreshold = $thresholds.lines
    $branchThreshold = $thresholds.branches
    
    $passed = $true
    
    Write-Host "`n=== Threshold Validation ===" -ForegroundColor Cyan
    
    if ($lineRate -ge $lineThreshold) {
        Write-Host "✅ Line coverage ($lineRate%) meets threshold ($lineThreshold%)" -ForegroundColor Green
    } else {
        Write-Host "❌ Line coverage ($lineRate%) below threshold ($lineThreshold%)" -ForegroundColor Red
        $passed = $false
    }
    
    if ($branchRate -ge $branchThreshold) {
        Write-Host "✅ Branch coverage ($branchRate%) meets threshold ($branchThreshold%)" -ForegroundColor Green
    } else {
        Write-Host "❌ Branch coverage ($branchRate%) below threshold ($branchThreshold%)" -ForegroundColor Red
        $passed = $false
    }
    
    # Detailed analysis if verbose
    if ($Verbose) {
        Write-Host "`n=== Detailed Analysis ===" -ForegroundColor Cyan
        
        # Parse individual classes
        $classes = $coverageXml.coverage.packages.package.classes.class
        $classResults = @()
        
        foreach ($class in $classes) {
            $className = $class.name
            $classLineRate = [math]::Round([double]$class.'line-rate' * 100, 2)
            $classBranchRate = [math]::Round([double]$class.'branch-rate' * 100, 2)
            
            $classResults += [PSCustomObject]@{
                Class = $className
                LineRate = $classLineRate
                BranchRate = $classBranchRate
            }
        }
        
        # Show classes with low coverage
        $lowCoverageClasses = $classResults | Where-Object { $_.LineRate -lt $lineThreshold -or $_.BranchRate -lt $branchThreshold }
        
        if ($lowCoverageClasses) {
            Write-Host "`nClasses below threshold:" -ForegroundColor Yellow
            $lowCoverageClasses | Format-Table -AutoSize
        }
        
        # Show top performers
        Write-Host "`nTop performing classes:" -ForegroundColor Green
        $classResults | Sort-Object LineRate -Descending | Select-Object -First 5 | Format-Table -AutoSize
    }
    
    # Final result
    Write-Host "`n=== Final Result ===" -ForegroundColor Cyan
    if ($passed) {
        Write-Host "✅ All coverage thresholds met!" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "❌ Coverage thresholds not met" -ForegroundColor Red
        if ($FailOnThreshold) {
            Write-Host "Build should fail due to insufficient coverage" -ForegroundColor Red
            exit 1
        } else {
            Write-Host "Continuing despite insufficient coverage (FailOnThreshold disabled)" -ForegroundColor Yellow
            exit 0
        }
    }
    
} catch {
    Write-Error "Failed to parse coverage file: $_"
    exit 1
}