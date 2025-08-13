# Project Structure & Architecture

## Folder Organization

```
ClutterFlock/
├── Core/                    # Business logic layer
│   ├── CacheManager.cs      # File/hash caching system
│   ├── DuplicateAnalyzer.cs # Core duplicate detection engine
│   ├── FileComparer.cs      # File comparison utilities
│   ├── FolderScanner.cs     # Recursive folder scanning
│   └── ProjectManager.cs    # Project save/load functionality
├── Models/                  # Data models and DTOs
│   └── DataModels.cs        # Core data structures
├── Services/                # Service interfaces
│   └── Interfaces.cs        # Service contracts
├── ViewModels/              # MVVM ViewModels
│   └── MainViewModel.cs     # Main window ViewModel
├── .kiro/                   # Kiro specifications and steering
│   ├── specs/               # Feature specifications
│   └── steering/            # Development guidance
├── Screenshot/              # UI screenshots
├── App.xaml[.cs]           # WPF application entry point
├── MainWindow.xaml[.cs]    # Main UI window
└── AssemblyInfo.cs         # Assembly metadata
```

## Architecture Patterns

### MVVM (Model-View-ViewModel)
- **Views:** XAML files with minimal code-behind
- **ViewModels:** Handle UI logic, data binding, and commands
- **Models:** Data structures and business entities
- **Services:** Business logic abstraction via interfaces

### Layered Architecture
- **UI Layer:** WPF views and ViewModels
- **Core Layer:** Business logic and algorithms
- **Services Layer:** Interface contracts and implementations
- **Models Layer:** Data transfer objects and entities

## Naming Conventions

### Files & Classes
- **PascalCase** for all class names, methods, properties
- **Interface prefix:** `I` (e.g., `ICacheManager`)
- **Async suffix:** Methods returning `Task` end with `Async`
- **Private fields:** `_camelCase` with underscore prefix

### Folders
- **PascalCase** for folder names
- **Logical grouping** by responsibility (Core, Models, Services, etc.)

## Key Design Principles

### Separation of Concerns
- UI logic separated from business logic
- Caching abstracted behind interfaces (ICacheManager, IDuplicateAnalyzer)
- File operations isolated in dedicated classes
- MVVM pattern with ViewModels handling UI logic and data binding

### Async/Await Pattern
- All long-running operations are async with proper cancellation support
- UI thread remains responsive during processing via Dispatcher.BeginInvoke
- Background processing using Task.Run for CPU-intensive work
- Progress reporting with IProgress<AnalysisProgress> interface

### Error Handling
- Graceful degradation for file access errors (UnauthorizedAccessException, IOException)
- User-friendly error messages via StatusMessage property
- No crashes on permission/IO issues - operations continue with remaining files
- Safe cleanup of background operations during application shutdown

### Performance Optimization
- Parallel processing where appropriate (Parallel.ForEach with MaxDegreeOfParallelism)
- Multi-level caching to avoid redundant file operations:
  - Folder File Cache: Dictionary mapping folder paths to file lists
  - Hash Cache: Dictionary mapping file paths to SHA-256 hashes
  - Folder Info Cache: Pre-computed folder metadata (size, file count)
- Progress reporting for long operations with cancellation support
- Memory-efficient data structures with ListView virtualization
- Batched UI updates to maintain responsiveness during large operations

### Data Integrity & Safety
- Read-only operation - never modifies user files
- SHA-256 hash verification for byte-level accuracy
- Thread-safe data structures for concurrent operations
- Proper resource cleanup and disposal patterns

## Code Style Guidelines

### Threading
- Use `async/await` for all I/O operations
- `CancellationToken` support for long operations
- `Parallel.ForEach` for CPU-intensive work
- UI updates via `Dispatcher.BeginInvoke`

### XAML Conventions
- Data binding over code-behind manipulation
- Command pattern for user interactions
- CollectionViewSource for advanced sorting
- Responsive layout with proper sizing