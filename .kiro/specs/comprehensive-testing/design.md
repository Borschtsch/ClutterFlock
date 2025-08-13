# Design Document

## Overview

The comprehensive testing system will establish a robust testing framework for ClutterFlock that includes unit tests, integration tests, performance tests, and development workflow guidance. The design leverages the existing MSTest infrastructure and extends it with comprehensive coverage across all core components.

The system will provide three main testing categories:
- **Unit Tests**: Isolated testing of individual components with mocked dependencies
- **Integration Tests**: End-to-end workflow testing with real file system interactions
- **Performance Tests**: Validation of system performance requirements and scalability

Additionally, a steering document will guide developers through test-driven development practices, ensuring consistent testing workflows and proper handling of test failures.

## Architecture

### Test Project Structure

The existing `ClutterFlock.Tests` project will be expanded with the following organization:

```
ClutterFlock.Tests/
├── Unit/                           # Unit tests with mocked dependencies
│   ├── Core/
│   │   ├── DuplicateAnalyzerTests.cs
│   │   ├── FileComparerTests.cs
│   │   ├── FolderScannerTests.cs
│   │   ├── CacheManagerTests.cs
│   │   └── ProjectManagerTests.cs
│   ├── ViewModels/
│   │   └── MainViewModelTests.cs
│   └── Services/
│       └── ErrorRecoveryServiceTests.cs
├── Integration/                    # End-to-end workflow tests
│   ├── FullWorkflowTests.cs
│   ├── ProjectPersistenceTests.cs
│   └── CancellationTests.cs
├── Performance/                    # Performance and scalability tests
│   ├── LargeDatasetTests.cs
│   ├── MemoryUsageTests.cs
│   └── UIResponsivenessTests.cs
├── TestUtilities/                  # Shared test infrastructure
│   ├── MockFileSystem.cs
│   ├── TestDataGenerator.cs
│   ├── MockCacheManager.cs
│   ├── MockErrorRecoveryService.cs
│   └── PerformanceTestHelper.cs
├── TestData/                       # Sample test files and folders
│   ├── SampleFiles/
│   └── ProjectFiles/
└── SimilarityCalculationTests.cs   # Existing test (keep as-is)
```

### Testing Framework Components

#### 1. Mock Infrastructure

**MockFileSystem**: Provides controllable file system operations for unit testing
- Simulates file and directory operations without actual I/O
- Supports error injection for testing error handling paths
- Configurable delays for testing async operations

**MockCacheManager**: Test double for ICacheManager interface
- Provides predictable cache behavior for unit tests
- Supports verification of cache operations
- Configurable cache hit/miss scenarios

**MockErrorRecoveryService**: Test implementation of IErrorRecoveryService
- Captures error handling calls for verification
- Configurable recovery actions for testing different scenarios

#### 2. Test Data Management

**TestDataGenerator**: Creates sample file structures and data for testing
- Generates temporary test directories with known file structures
- Creates files with specific sizes, dates, and content hashes
- Provides cleanup mechanisms for test isolation

**PerformanceTestHelper**: Utilities for performance testing
- Memory usage monitoring
- Execution time measurement
- Progress reporting validation
- Cancellation token testing

#### 3. Test Categories and Attributes

Tests will be categorized using MSTest attributes:
- `[TestCategory("Unit")]` - Fast, isolated unit tests
- `[TestCategory("Integration")]` - Slower integration tests requiring file system
- `[TestCategory("Performance")]` - Performance validation tests
- `[TestCategory("LongRunning")]` - Tests that may take significant time

## Components and Interfaces

### Unit Test Components

#### Core Component Tests

**DuplicateAnalyzerTests**
- Tests duplicate detection logic with various file scenarios
- Validates hash comparison accuracy
- Tests progress reporting and cancellation
- Verifies error handling for inaccessible files
- Tests folder aggregation and similarity calculations

**FileComparerTests**
- Tests file comparison logic for detail view
- Validates file status determination (duplicate, unique, missing)
- Tests sorting and filtering operations
- Verifies error handling for file access issues

**FolderScannerTests**
- Tests recursive folder scanning logic
- Validates progress reporting during scanning
- Tests cancellation behavior
- Verifies error handling for permission issues
- Tests caching integration

**CacheManagerTests**
- Tests all cache operations (get, set, clear, remove)
- Validates thread safety of concurrent operations
- Tests cache invalidation scenarios
- Verifies memory management and cleanup

**ProjectManagerTests**
- Tests project save/load operations
- Validates JSON serialization/deserialization
- Tests backward compatibility with legacy formats
- Verifies error handling for corrupted files

#### ViewModel Tests

**MainViewModelTests**
- Tests command execution and data binding
- Validates UI state management
- Tests progress reporting integration
- Verifies error message handling

### Integration Test Components

**FullWorkflowTests**
- Tests complete folder analysis workflows
- Validates end-to-end duplicate detection
- Tests project save/load with real data
- Verifies UI integration points

**ProjectPersistenceTests**
- Tests complete project lifecycle (create, save, load, modify)
- Validates data integrity across save/load cycles
- Tests migration from legacy formats

**CancellationTests**
- Tests cancellation behavior across all operations
- Validates proper resource cleanup
- Tests UI responsiveness during cancellation

### Performance Test Components

**LargeDatasetTests**
- Tests processing speed with 10,000+ files
- Validates memory usage under load
- Tests scalability with 100,000+ subfolders
- Measures UI responsiveness during heavy operations

**MemoryUsageTests**
- Monitors memory consumption during operations
- Tests for memory leaks in long-running operations
- Validates cache memory management

**UIResponsivenessTests**
- Measures UI update frequency and timing
- Tests progress reporting accuracy
- Validates cancellation responsiveness

## Data Models

### Test Data Models

```csharp
public class TestFileStructure
{
    public string RootPath { get; set; }
    public List<TestFolder> Folders { get; set; }
    public List<TestFile> Files { get; set; }
}

public class TestFolder
{
    public string Path { get; set; }
    public List<string> Files { get; set; }
    public long TotalSize { get; set; }
    public DateTime? LatestModification { get; set; }
}

public class TestFile
{
    public string Path { get; set; }
    public string Name { get; set; }
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public string ExpectedHash { get; set; }
}

public class PerformanceMetrics
{
    public TimeSpan ExecutionTime { get; set; }
    public long MemoryUsage { get; set; }
    public int FilesProcessed { get; set; }
    public double FilesPerSecond { get; set; }
    public List<ProgressUpdate> ProgressUpdates { get; set; }
}
```

## Error Handling

### Test Error Scenarios

The testing framework will validate error handling for:

1. **File System Errors**
   - UnauthorizedAccessException (permission denied)
   - IOException (file in use, network issues)
   - DirectoryNotFoundException (missing directories)
   - PathTooLongException (path length limits)

2. **Data Corruption Scenarios**
   - Invalid project file formats
   - Corrupted JSON data
   - Missing required fields

3. **Resource Constraints**
   - Out of memory conditions
   - Disk space limitations
   - Network connectivity issues

4. **Cancellation Scenarios**
   - User-initiated cancellation
   - Timeout-based cancellation
   - Application shutdown during operations

### Error Recovery Testing

Tests will verify that the application:
- Continues processing after recoverable errors
- Provides meaningful error messages to users
- Maintains data integrity during error conditions
- Properly cleans up resources after errors

## Testing Strategy

### Test Execution Strategy

**Fast Feedback Loop**
- Unit tests run in under 30 seconds total
- Integration tests complete within 2 minutes
- Performance tests may take up to 10 minutes

**Parallel Execution**
- Unit tests run in parallel using MSTest parallelization
- Integration tests run sequentially to avoid file system conflicts
- Performance tests run in isolation

**Test Data Management**
- Each test creates isolated temporary directories
- Automatic cleanup after test completion
- Shared test data for common scenarios

### Coverage Requirements

**Code Coverage Targets**
- Core business logic: 90%+ coverage
- ViewModels: 80%+ coverage
- Services: 85%+ coverage
- Overall project: 80%+ coverage

**Functional Coverage**
- All public methods and properties tested
- All error handling paths validated
- All async operations tested with cancellation
- All UI commands and data binding tested

### Continuous Integration Integration

The testing framework will support:
- Command-line execution via `dotnet test`
- Test filtering by category
- Code coverage reporting
- Test result export (XML, HTML)
- Integration with build pipelines

## Steering Document Design

### Development Workflow Guidance

The steering document will provide clear instructions for:

1. **Pre-Change Testing**
   - Run existing tests before making changes
   - Identify baseline test results
   - Document any pre-existing failures

2. **Post-Change Testing**
   - Run full test suite after code changes
   - Analyze test failures and build errors
   - Report issues rather than immediately fixing tests

3. **Test Failure Handling**
   - Categorize failures (test issue vs. code issue)
   - Document potential fixes and their implications
   - Escalate to human decision-maker for resolution

4. **New Feature Testing**
   - Require corresponding tests for new functionality
   - Ensure adequate coverage of new code paths
   - Validate integration with existing components

### Automation Integration

The steering document will guide integration with:
- Build process automation
- Pre-commit hooks (optional)
- Continuous integration pipelines
- Code review processes

This comprehensive testing design ensures robust validation of ClutterFlock's functionality while providing clear guidance for maintaining code quality through test-driven development practices.