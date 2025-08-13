# Design Document

## Overview

This design extends the existing ClutterFlock architecture to add comprehensive documentation, enhanced user experience features, and advanced analysis capabilities. The design maintains the current MVVM architecture while adding new services for export functionality, performance monitoring, and user guidance systems.

## Architecture

### Current Architecture Foundation
The application follows a layered MVVM architecture:
- **UI Layer**: WPF MainWindow with simplified code-behind
- **ViewModel Layer**: MainViewModel handling UI logic and data binding
- **Services Layer**: Interface abstractions for all business operations
- **Core Layer**: Business logic implementations (CacheManager, DuplicateAnalyzer, etc.)
- **Models Layer**: Data structures and DTOs

### New Components Integration
The enhancements will integrate seamlessly with the existing architecture by:
1. Adding new service interfaces to the Services layer
2. Implementing new core services following existing patterns
3. Extending the MainViewModel with new properties and commands
4. Adding new models for export and reporting functionality

## Components and Interfaces

### 1. Data Export and Reporting System

#### IExportService Interface
```csharp
public interface IExportService
{
    Task<bool> ExportFolderMatchesToCsvAsync(string filePath, List<FolderMatch> matches, ExportOptions options);
    Task<bool> ExportFileComparisonToCsvAsync(string filePath, List<FileDetailInfo> fileDetails, string leftFolder, string rightFolder);
    Task<bool> ExportAnalysisSummaryAsync(string filePath, AnalysisSummary summary);
    Task<string> GenerateActionScriptAsync(List<BatchAction> actions, ScriptFormat format);
}
```

#### Export Models
```csharp
public class ExportOptions
{
    public bool IncludeMetadata { get; set; } = true;
    public bool IncludePerformanceStats { get; set; } = false;
    public FilterCriteria? AppliedFilters { get; set; }
}

public class AnalysisSummary
{
    public DateTime AnalysisDate { get; set; }
    public List<string> ScanFolders { get; set; }
    public int TotalFoldersAnalyzed { get; set; }
    public int DuplicateGroupsFound { get; set; }
    public long PotentialSpaceSavingsBytes { get; set; }
    public FilterCriteria AppliedFilters { get; set; }
}

public class BatchAction
{
    public string LeftFolder { get; set; }
    public string RightFolder { get; set; }
    public BatchActionType Action { get; set; }
    public string Reason { get; set; }
}

public enum BatchActionType
{
    KeepLeft,
    KeepRight,
    ManualReview,
    Skip
}
```

### 2. Enhanced Error Handling and Recovery

#### IErrorRecoveryService Interface
```csharp
public interface IErrorRecoveryService
{
    Task<RecoveryAction> HandleFileAccessError(string filePath, Exception error);
    Task<RecoveryAction> HandleNetworkError(string networkPath, Exception error);
    Task<RecoveryAction> HandleResourceConstraintError(ResourceConstraintType type, Exception error);
    void LogSkippedItem(string path, string reason);
    ErrorSummary GetErrorSummary();
}
```

#### Error Handling Models
```csharp
public class RecoveryAction
{
    public RecoveryActionType Type { get; set; }
    public string Message { get; set; }
    public string SuggestedSolution { get; set; }
    public bool ShouldRetry { get; set; }
    public TimeSpan RetryDelay { get; set; }
}

public enum RecoveryActionType
{
    Skip,
    Retry,
    RetryWithElevation,
    ReduceParallelism,
    PauseAndWait,
    Abort
}

public class ErrorSummary
{
    public int SkippedFiles { get; set; }
    public int PermissionErrors { get; set; }
    public int NetworkErrors { get; set; }
    public List<string> SkippedPaths { get; set; }
    public List<string> ErrorMessages { get; set; }
}
```

### 3. Performance Monitoring and Optimization

#### IPerformanceMonitor Interface
```csharp
public interface IPerformanceMonitor
{
    void StartMonitoring();
    void StopMonitoring();

    bool ShouldReduceParallelism();
    bool ShouldReduceBatchSize();
    int GetOptimalParallelismLevel();
    void RecordFileProcessed(long fileSize);
}
```

#### Implementation Strategy
- Monitor system memory usage using PerformanceCounter
- Track processing speed and adjust parallelism dynamically
- Implement adaptive batch sizing based on available memory
- Provide user notifications when performance optimizations are applied

### 4. Enhanced Project Management

#### Extended ProjectData Model
```csharp
public class EnhancedProjectData : ProjectData
{
    public ProjectMetadata Metadata { get; set; }
    public List<string> MissingFolders { get; set; }
    public List<string> ModifiedFolders { get; set; }
    public AnalysisSummary LastAnalysisSummary { get; set; }
}

public class ProjectMetadata
{
    public string ProjectName { get; set; }
    public string Description { get; set; }
    public DateTime LastModified { get; set; }
    public int TotalFoldersScanned { get; set; }
    public long TotalFilesAnalyzed { get; set; }
    public TimeSpan LastAnalysisDuration { get; set; }
}
```

#### IEnhancedProjectManager Interface
```csharp
public interface IEnhancedProjectManager : IProjectManager
{
    Task<ProjectValidationResult> ValidateProjectAsync(string filePath);
    Task<List<RecentProject>> GetRecentProjectsAsync();
    Task<bool> DetectFolderChangesAsync(ProjectData project);
    Task<ProjectData> CreateIncrementalUpdateAsync(ProjectData baseProject, List<string> changedFolders);
}
```

### 5. Documentation and Help System

#### IHelpService Interface
```csharp
public interface IHelpService
{
    Task<string> GetQuickStartGuideAsync();
    Task<string> GetFeatureHelpAsync(string featureName);
    Task<List<HelpTopic>> GetTroubleshootingTopicsAsync();
    Task<string> GetContextualHelpAsync(string context);
    bool IsFirstRun();
    void MarkFirstRunComplete();
}
```

#### Help System Models
```csharp
public class HelpTopic
{
    public string Title { get; set; }
    public string Content { get; set; }
    public List<string> Keywords { get; set; }
    public HelpTopicType Type { get; set; }
}

public enum HelpTopicType
{
    QuickStart,
    Feature,
    Troubleshooting,
    FAQ,
    Advanced
}
```

### 6. Batch Operations System

#### IBatchOperationService Interface
```csharp
public interface IBatchOperationService
{
    void MarkFolderPair(FolderMatch match, BatchActionType action, string reason);
    List<BatchAction> GetMarkedActions();
    void ClearMarkedActions();
    Task<ValidationResult> ValidateActionsAsync(List<BatchAction> actions);
    Task<string> GenerateActionReportAsync(List<BatchAction> actions);
}
```

## Data Models

### Enhanced Models for New Features

#### TimeEstimation Model
```csharp
public class TimeEstimation
{
    public TimeSpan EstimatedRemaining { get; set; }
    public double ConfidenceLevel { get; set; }
    public string EstimationBasis { get; set; }
    public DateTime EstimatedCompletion { get; set; }
}
```

#### InsightData Model
```csharp
public class InsightData
{
    public long TotalPotentialSavings { get; set; }
    public FolderMatch LargestDuplicateGroup { get; set; }
    public FolderMatch MostRecentDuplicate { get; set; }
    public List<FolderMatch> HighPriorityMatches { get; set; }
    public Dictionary<string, int> DuplicatesByExtension { get; set; }
}
```

## Error Handling

### Comprehensive Error Recovery Strategy

1. **File Access Errors**
   - Detect permission issues and suggest running as administrator
   - Handle locked files by skipping and continuing
   - Provide detailed error messages with file paths

2. **Network Connectivity Issues**
   - Detect network drive disconnections
   - Implement pause/resume functionality for network operations
   - Provide retry mechanisms with exponential backoff

3. **Resource Constraint Handling**
   - Monitor memory usage and reduce batch sizes automatically
   - Detect high CPU usage and offer to reduce parallelism
   - Implement graceful degradation for low-resource scenarios

4. **Data Integrity Protection**
   - Validate project files before loading
   - Detect corrupted cache data and offer to rebuild
   - Ensure safe cancellation without data loss

## Testing Strategy

### Unit Testing Approach
1. **Service Layer Testing**
   - Mock all dependencies using interfaces
   - Test each service in isolation
   - Validate error handling and edge cases

2. **Integration Testing**
   - Test service interactions
   - Validate data flow between components
   - Test performance under various conditions

3. **UI Testing**
   - Test ViewModel behavior and data binding
   - Validate command execution and state management
   - Test progress reporting and user feedback

### Performance Testing
1. **Load Testing**
   - Test with large datasets (100,000+ files)
   - Validate memory usage patterns
   - Test cancellation and cleanup procedures

2. **Stress Testing**
   - Test under low memory conditions
   - Validate behavior with slow storage devices
   - Test network interruption scenarios

## Implementation Phases

### Phase 1: Export and Reporting System
- Implement IExportService
- Add CSV export functionality
- Create analysis summary generation

### Phase 2: Error Handling and Recovery
- Implement IErrorRecoveryService
- Add comprehensive error handling throughout
- Implement retry and recovery mechanisms

### Phase 3: Enhanced Project Management
- Extend project validation and metadata
- Implement recent projects functionality
- Add incremental update capabilities

### Phase 4: Documentation and Help System
- Implement IHelpService
- Create embedded help content
- Add contextual help integration

### Phase 5: Batch Operations
- Implement IBatchOperationService
- Add batch action marking UI
- Create action validation and reporting

This design maintains compatibility with the existing architecture while adding powerful new capabilities that enhance both user experience and developer maintainability.