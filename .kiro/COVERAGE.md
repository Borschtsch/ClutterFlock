# Code Coverage Guide for ClutterFlock

This document provides comprehensive guidance for maintaining and monitoring code coverage in the ClutterFlock project.

## Current Coverage Status

As of the latest measurement:
- **Line Coverage**: 72.51% (1013/1397 lines)
- **Branch Coverage**: 52.8% (276/522 branches)
- **Method Coverage**: 84.58%

### Coverage by Component

| Component | Line Coverage | Status | Notes |
|-----------|---------------|--------|-------|
| **Core Business Logic** | | | |
| FileComparer | 100% | ✅ Excellent | Complete coverage |
| CacheManager | 97% | ✅ Excellent | Near-complete coverage |
| ErrorRecoveryService | 98%+ | ✅ Excellent | Comprehensive tests |
| ProjectManager | 95%+ | ✅ Excellent | Well tested |
| **Analysis Components** | | | |
| DuplicateAnalyzer | 87% | ⚠️ Good | 36 lines uncovered |
| FolderScanner | 83% | ⚠️ Good | 27 lines uncovered |
| **UI Layer** | | | |
| MainViewModel | 56% | ⚠️ Needs Work | 135 lines uncovered |
| MainWindow.xaml.cs | 0% | ❌ Expected | UI event handlers |
| App.xaml | 0% | ❌ Expected | WPF initialization |
| **Models** | | | |
| FilterCriteria | 100% | ✅ Excellent | Complete |
| RecoveryAction | 100% | ✅ Excellent | Complete |
| FolderMatch | 73% | ⚠️ Good | 6 lines uncovered |

## Coverage Thresholds

The project maintains different coverage thresholds for different types of code:

### Global Thresholds (Required)
- **Line Coverage**: 100%
- **Branch Coverage**: 100%
- **Method Coverage**: 100%

### Component-Specific Thresholds
- **Core Business Logic**: 100% line, 100% branch
- **Models**: 100% line, 100% branch  
- **ViewModels**: 100% line, 100% branch
- **UI Layer**: Excluded from coverage requirements

## Running Coverage Analysis

### Quick Coverage Check
```bash
# Run tests with coverage (VS Code Task)
Ctrl+Shift+P → "Tasks: Run Task" → "test with coverage"

# Or via command line
dotnet test ClutterFlock.Tests/ClutterFlock.Tests.csproj -c Release --no-build --verbosity normal /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=TestResults/coverage /p:Include="[ClutterFlock]*"
```

### Full Coverage Report with HTML
```bash
# Using VS Code Task (Recommended)
Ctrl+Shift+P → "Tasks: Run Task" → "coverage report"

# Or using PowerShell script
.\scripts\coverage-report.ps1

# Skip build if already built
.\scripts\coverage-report.ps1 -SkipBuild
```

### Coverage Validation
```bash
# Validate against thresholds
Ctrl+Shift+P → "Tasks: Run Task" → "validate coverage"

# Or using PowerShell script
.\scripts\validate-coverage.ps1 -Verbose
```

## Coverage Regression Prevention

### Automated Validation
The project includes automated coverage validation that:

1. **Runs after every test execution**
2. **Validates against minimum thresholds**
3. **Fails builds if coverage drops below acceptable levels**
4. **Provides detailed reports on coverage gaps**

### Build Integration
Coverage validation is integrated into the build process through:

- **VS Code Tasks**: Automated coverage collection and validation
- **PowerShell Scripts**: Standalone coverage analysis tools
- **Configuration Files**: Centralized threshold management

### Continuous Monitoring
To prevent coverage regression:

1. **Run coverage analysis before committing code**
2. **Review coverage reports for new code**
3. **Ensure new features include comprehensive tests**
4. **Monitor coverage trends over time**

## Improving Coverage

### Priority Areas for Improvement

1. **MainViewModel (56% → 70%+ target)**
   - Add tests for command implementations
   - Test property change notifications
   - Cover error handling scenarios
   - Test async operations and cancellation

2. **DuplicateAnalyzer (87% → 95%+ target)**
   - Test edge cases in duplicate detection
   - Cover error handling paths
   - Test cancellation scenarios
   - Add boundary condition tests

3. **FolderScanner (83% → 95%+ target)**
   - Test file system error scenarios
   - Cover permission denied cases
   - Test progress reporting edge cases
   - Add cancellation handling tests

### Testing Strategies

#### For ViewModels
```csharp
[Test]
public async Task Command_WhenExecuted_UpdatesProperties()
{
    // Arrange
    var viewModel = new MainViewModel(mockServices);
    
    // Act
    await viewModel.SomeCommand.ExecuteAsync();
    
    // Assert
    Assert.That(viewModel.SomeProperty, Is.EqualTo(expectedValue));
}
```

#### For Core Logic
```csharp
[Test]
public void Method_WithEdgeCase_HandlesGracefully()
{
    // Arrange
    var service = new ServiceUnderTest();
    var edgeCaseInput = CreateEdgeCaseData();
    
    // Act & Assert
    Assert.DoesNotThrow(() => service.Method(edgeCaseInput));
}
```

#### For Error Scenarios
```csharp
[Test]
public void Method_WhenFileSystemError_ContinuesProcessing()
{
    // Arrange
    mockFileSystem.Setup(x => x.ReadFile(It.IsAny<string>()))
                  .Throws<UnauthorizedAccessException>();
    
    // Act & Assert
    Assert.DoesNotThrow(() => service.ProcessFiles(paths));
}
```

## Coverage Exclusions

The following code is excluded from coverage requirements:

### Automatically Excluded
- **WPF Generated Code**: `*.g.cs`, `*.g.i.cs`
- **Designer Files**: `*.Designer.cs`
- **Assembly Metadata**: `AssemblyInfo.cs`

### Manually Excluded (Justified)
- **App.xaml**: WPF application initialization
- **MainWindow.xaml.cs**: UI event handlers and code-behind
- **XAML Code-Behind**: Direct UI manipulation code

### Exclusion Rationale
UI layer code is excluded because:
1. **Difficult to test in isolation** without full UI framework
2. **Low business logic complexity** - mostly event routing
3. **High maintenance cost** for UI automation tests
4. **Better tested through integration/manual testing**

## Tools and Configuration

### Coverage Tools
- **Coverlet**: .NET coverage collection tool
- **ReportGenerator**: HTML report generation
- **Cobertura**: XML coverage format for CI/CD integration

### Configuration Files
- **coverage-config.json**: Coverage thresholds and settings
- **scripts/validate-coverage.ps1**: Automated validation script
- **scripts/coverage-report.ps1**: Report generation script
- **.vscode/tasks.json**: VS Code task integration

### VS Code Integration
Available tasks:
- `test with coverage`: Basic coverage collection
- `test with coverage + HTML report`: Full report with browser opening
- `coverage report`: Complete coverage pipeline
- `validate coverage`: Threshold validation only
- `Full Coverage Pipeline`: End-to-end coverage workflow

## Troubleshooting

### Common Issues

#### Coverage File Not Found
```
Error: Coverage file not found: ClutterFlock.Tests/TestResults/coverage.cobertura.xml
```
**Solution**: Run tests with coverage first:
```bash
dotnet test ClutterFlock.Tests/ClutterFlock.Tests.csproj /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:CoverletOutput=ClutterFlock.Tests/TestResults/coverage /p:Include="[ClutterFlock]*"
```

#### ReportGenerator Not Found
```
Error: ReportGenerator not found
```
**Solution**: Install the global tool:
```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

#### Low Coverage Warnings
When coverage drops below thresholds:
1. **Review the coverage report** to identify uncovered code
2. **Add targeted tests** for uncovered lines and branches
3. **Consider if exclusions are appropriate** for UI or generated code
4. **Update thresholds** if they're unrealistic for the code type

### Getting Help

For coverage-related issues:
1. **Check the HTML coverage report** for detailed line-by-line analysis
2. **Run validation with -Verbose flag** for detailed output
3. **Review this documentation** for configuration and usage guidance
4. **Check VS Code tasks** for pre-configured coverage workflows

## Maintenance Schedule

### Regular Tasks
- **Weekly**: Review coverage trends and identify declining areas
- **Before Releases**: Ensure coverage meets minimum thresholds
- **After Major Changes**: Update coverage baselines and thresholds
- **Quarterly**: Review and update coverage exclusions and thresholds

### Coverage Goals
- **Current Target**: Achieve 100% line coverage for all production code
- **Ongoing**: Maintain 100% coverage for all new code
- **Quality Focus**: Ensure meaningful tests that validate business logic and catch regressions

Remember: **Coverage is a quality metric, not a goal**. Focus on meaningful tests that validate business logic and catch regressions, rather than just achieving high coverage numbers.