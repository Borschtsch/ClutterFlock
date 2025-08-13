# Technology Stack

## Framework & Platform
- **.NET 9.0** with Windows Desktop Runtime
- **WPF (Windows Presentation Foundation)** for UI
- **Windows Forms** integration for folder browser dialogs
- **Target Platform:** Windows 10 version 26100.0 or later
- **Architecture:** x64 primary, x86 secondary

## Key Libraries & Dependencies
- `System.Text.Json` for project file serialization
- `System.Security.Cryptography` for SHA-256 hashing
- Built-in .NET threading and async/await patterns
- No external NuGet packages (minimal dependency approach)

## Build System
- **MSBuild** via Visual Studio or .NET CLI
- Project file: `ClutterFlock.csproj`
- Solution file: `ClutterFlock.sln`

## Common Commands

### Build
```cmd
dotnet build
dotnet build --configuration Release
```

### Run
```cmd
dotnet run
```

### Publish
```cmd
dotnet publish --configuration Release --runtime win-x64 --self-contained
```

## Development Environment
- **Visual Studio 2022 Community Edition** (primary)
- **GitHub Copilot** integration for AI assistance
- **Windows-only development** (no cross-platform considerations)

## Performance Considerations
- Multi-threaded file processing using `Parallel.ForEach`
- Configurable parallelism based on processor count (MaxParallelism = ProcessorCount - 1)
- Async/await patterns for UI responsiveness with proper Dispatcher usage
- Memory-efficient caching with cleanup mechanisms
- Progress reporting with cancellation token support
- ListView virtualization for large result sets
- Batched UI updates (every 10-50 items) to maintain responsiveness
- Background processing with `Task.Run` for CPU-intensive operations

## File System Requirements
- Read access to user-specified folders
- Unicode path support with case-insensitive comparison
- Graceful handling of file access errors (permissions, missing files)
- Support for network drives and UNC paths
- No network communication - all processing local

## Data Formats
- **Project Files:** JSON-based .cfp format (ClutterFlock Project) using System.Text.Json, with backward compatibility for legacy .dfp files
- **Caching:** Dictionary-based caching for folder files, file metadata, and SHA-256 hashes
- **Hash Algorithm:** SHA-256 cryptographic hashing for file integrity verification