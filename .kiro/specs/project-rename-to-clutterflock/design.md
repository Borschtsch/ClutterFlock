# Design Document

## Overview

This design outlines the comprehensive renaming strategy for transforming FolderDupFinder to ClutterFlock. The renaming involves systematic updates across multiple layers: file system structure, code namespaces, user interface elements, documentation, and project configurations. The design ensures backward compatibility while establishing the new ClutterFlock brand identity throughout the application.

## Architecture

### Renaming Strategy Overview
The renaming follows a layered approach to ensure consistency and minimize the risk of breaking changes:

1. **File System Layer**: Rename physical files and directories
2. **Project Configuration Layer**: Update project files, solution files, and build configurations
3. **Code Structure Layer**: Update namespaces, class references, and using statements
4. **User Interface Layer**: Update window titles, dialog text, and user-visible strings
5. **Documentation Layer**: Update all documentation and help content
6. **Data Format Layer**: Introduce new file extensions while maintaining backward compatibility

### Impact Analysis
The renaming affects the following components:
- **Project Files**: .csproj, .sln, .code-workspace files
- **Source Code**: All .cs files with namespace declarations and references
- **XAML Files**: Window definitions and namespace references
- **Documentation**: README, steering files, and specification documents
- **Build Outputs**: Executable names and assembly metadata
- **User Data**: Project file extensions and dialog filters

## Components and Interfaces

### 1. File System Renaming Strategy

#### Primary Files to Rename
```
FolderDupFinder.csproj → ClutterFlock.csproj
FolderDupFinder.sln → ClutterFlock.sln
FolderDupFinder.code-workspace → ClutterFlock.code-workspace
```

#### Directory Structure (Unchanged)
The existing directory structure remains the same:
```
ClutterFlock/
├── Core/
├── Models/
├── Services/
├── ViewModels/
├── .kiro/
└── Screenshot/
```

### 2. Namespace Transformation Map

#### Current Namespace Structure
```csharp
FolderDupFinder
├── FolderDupFinder.Core
├── FolderDupFinder.Models
├── FolderDupFinder.Services
├── FolderDupFinder.ViewModels
```

#### New Namespace Structure
```csharp
ClutterFlock
├── ClutterFlock.Core
├── ClutterFlock.Models
├── ClutterFlock.Services
├── ClutterFlock.ViewModels
```

### 3. User Interface Text Mapping

#### Window Titles and Headers
```
"Folder Duplicates Finder" → "ClutterFlock"
"Folder duplicates finder" → "ClutterFlock"
"FolderDupFinder" → "ClutterFlock"
```

#### File Dialog Filters
```
"Duplicate Folder Project (*.dfp)|*.dfp" → "ClutterFlock Project (*.cfp)|*.cfp|Legacy Project (*.dfp)|*.dfp"
```

#### Status Messages and Labels
- Update any hardcoded references to the old name in status messages
- Maintain functional text while updating branding references

### 4. Project File Extension Strategy

#### New Primary Format
- **Extension**: .cfp (ClutterFlock Project)
- **MIME Type**: application/x-clutterflock-project
- **Description**: ClutterFlock Project File

#### Backward Compatibility
- Continue supporting .dfp files for reading
- Offer migration prompts when opening .dfp files
- Default save format becomes .cfp

#### File Format Structure (Unchanged)
```json
{
  "scanFolders": [...],
  "folderFileCache": {...},
  "fileHashCache": {...},
  "folderInfoCache": {...},
  "createdDate": "...",
  "version": "1.0",
  "applicationName": "ClutterFlock"  // New field
}
```

### 5. Build Configuration Updates

#### Assembly Information
```csharp
[assembly: AssemblyTitle("ClutterFlock")]
[assembly: AssemblyDescription("Duplicate folder analysis tool")]
[assembly: AssemblyProduct("ClutterFlock")]
[assembly: AssemblyCompany("")]
```

#### Output Configuration
```xml
<PropertyGroup>
  <AssemblyName>ClutterFlock</AssemblyName>
  <RootNamespace>ClutterFlock</RootNamespace>
  <OutputType>WinExe</OutputType>
</PropertyGroup>
```

## Data Models

### Enhanced Project Data Model

#### Updated ProjectData Structure
```csharp
namespace ClutterFlock.Models
{
    public class ProjectData
    {
        public List<string> ScanFolders { get; set; } = new();
        public Dictionary<string, List<string>> FolderFileCache { get; set; } = new();
        public Dictionary<string, string> FileHashCache { get; set; } = new();
        public Dictionary<string, FolderInfo> FolderInfoCache { get; set; } = new();
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public string Version { get; set; } = "1.0";
        public string ApplicationName { get; set; } = "ClutterFlock"; // New field for identification
        public string LegacyApplicationName { get; set; } = "FolderDupFinder"; // For migration tracking
    }
}
```

### Migration Support Model

#### Project Migration Information
```csharp
namespace ClutterFlock.Models
{
    public class ProjectMigrationInfo
    {
        public bool IsLegacyProject { get; set; }
        public string OriginalFileName { get; set; }
        public string SuggestedNewFileName { get; set; }
        public DateTime MigrationDate { get; set; }
        public string SourceApplication { get; set; }
    }
}
```

## Error Handling

### Migration Error Scenarios

1. **File Access Issues During Renaming**
   - Handle locked files gracefully
   - Provide clear error messages with suggested solutions
   - Offer retry mechanisms for temporary issues

2. **Legacy Project Loading Failures**
   - Validate legacy .dfp file format
   - Handle corrupted or incompatible legacy files
   - Provide migration status and error reporting

3. **Namespace Compilation Errors**
   - Ensure all namespace references are updated consistently
   - Validate that no orphaned references remain
   - Handle circular dependencies during renaming

## Testing Strategy

### Renaming Validation Tests

1. **File System Tests**
   - Verify all files are renamed correctly
   - Ensure no broken file references
   - Validate project structure integrity

2. **Compilation Tests**
   - Ensure project compiles successfully after renaming
   - Verify all namespace references are resolved
   - Test that no compilation errors are introduced

3. **Functionality Tests**
   - Verify all existing functionality works after renaming
   - Test that UI displays new branding correctly
   - Ensure project save/load works with new format

4. **Backward Compatibility Tests**
   - Test loading of legacy .dfp files
   - Verify migration prompts work correctly
   - Ensure no data loss during format conversion

### User Experience Tests

1. **Branding Consistency**
   - Verify all user-visible text uses ClutterFlock
   - Check that window titles and dialogs are updated
   - Ensure help text and error messages are consistent

2. **File Association Tests**
   - Test that .cfp files are properly associated
   - Verify file icons and descriptions are correct
   - Ensure Windows Explorer integration works

## Implementation Strategy

### Phase 1: Project Structure Renaming
1. Rename primary project files (.csproj, .sln, .code-workspace)
2. Update project references and solution configuration
3. Verify build system works with new names

### Phase 2: Code Namespace Updates
1. Update all namespace declarations systematically
2. Update using statements throughout codebase
3. Update XAML namespace references and x:Class attributes

### Phase 3: User Interface Updates
1. Update window titles and dialog text
2. Update file dialog filters and extensions
3. Update status messages and user-visible strings

### Phase 4: Documentation Updates
1. Update README and main documentation
2. Update steering files and technical documentation
3. Update existing specification documents

### Phase 5: Build and Configuration Updates
1. Update assembly information and metadata
2. Update build configurations and output settings
3. Test final build and deployment process

### Phase 6: Validation and Testing
1. Comprehensive testing of renamed application
2. Backward compatibility testing with legacy files
3. User experience validation and final adjustments

## Risk Mitigation

### Potential Risks and Mitigation Strategies

1. **Broken References Risk**
   - **Mitigation**: Systematic approach with validation at each step
   - **Detection**: Compilation tests after each phase
   - **Recovery**: Maintain backup of original state

2. **User Data Loss Risk**
   - **Mitigation**: Maintain backward compatibility with .dfp files
   - **Detection**: Comprehensive testing with sample legacy files
   - **Recovery**: Provide data recovery tools if needed

3. **Build System Failures**
   - **Mitigation**: Update build configurations incrementally
   - **Detection**: Continuous integration testing
   - **Recovery**: Rollback capability for build configurations

4. **User Experience Disruption**
   - **Mitigation**: Maintain familiar functionality while updating branding
   - **Detection**: User experience testing and feedback
   - **Recovery**: Quick fixes for critical UX issues

This design ensures a smooth transition from FolderDupFinder to ClutterFlock while maintaining all existing functionality and providing a professional, consistent rebranding throughout the application.