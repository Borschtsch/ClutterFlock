using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClutterFlock.Models;

namespace ClutterFlock.Tests.TestUtilities
{
    /// <summary>
    /// Mock file system implementation for testing file operations without actual I/O
    /// </summary>
    public class MockFileSystem
    {
        private readonly Dictionary<string, MockDirectory> _directories = new();
        private readonly Dictionary<string, MockFile> _files = new();
        private readonly Dictionary<string, Exception> _errorInjections = new();
        private readonly Dictionary<string, TimeSpan> _delayInjections = new();

        public MockFileSystem()
        {
            // Always have root directories available
            _directories["C:\\"] = new MockDirectory("C:\\");
        }

        /// <summary>
        /// Creates a mock directory structure
        /// </summary>
        public void CreateDirectory(string path, DateTime? lastWriteTime = null)
        {
            var normalizedPath = NormalizePath(path);
            _directories[normalizedPath] = new MockDirectory(normalizedPath, lastWriteTime ?? DateTime.Now);
            
            // Ensure parent directories exist
            var parentPath = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrEmpty(parentPath) && !_directories.ContainsKey(parentPath))
            {
                CreateDirectory(parentPath);
            }
        }

        /// <summary>
        /// Creates a mock file with specified content and metadata
        /// </summary>
        public void CreateFile(string path, byte[] content, DateTime? lastWriteTime = null)
        {
            var normalizedPath = NormalizePath(path);
            var directory = Path.GetDirectoryName(normalizedPath);
            
            if (!string.IsNullOrEmpty(directory) && !_directories.ContainsKey(directory))
            {
                CreateDirectory(directory);
            }

            _files[normalizedPath] = new MockFile(normalizedPath, content, lastWriteTime ?? DateTime.Now);
        }

        /// <summary>
        /// Creates a mock file with text content
        /// </summary>
        public void CreateFile(string path, string content, DateTime? lastWriteTime = null)
        {
            CreateFile(path, System.Text.Encoding.UTF8.GetBytes(content), lastWriteTime);
        }

        /// <summary>
        /// Injects an error that will be thrown when accessing the specified path
        /// </summary>
        public void InjectError(string path, Exception error)
        {
            _errorInjections[NormalizePath(path)] = error;
        }

        /// <summary>
        /// Injects a delay that will occur when accessing the specified path
        /// </summary>
        public void InjectDelay(string path, TimeSpan delay)
        {
            _delayInjections[NormalizePath(path)] = delay;
        }

        /// <summary>
        /// Checks if a directory exists
        /// </summary>
        public async Task<bool> DirectoryExistsAsync(string path)
        {
            await SimulateDelayAndErrors(path);
            return _directories.ContainsKey(NormalizePath(path));
        }

        /// <summary>
        /// Checks if a file exists
        /// </summary>
        public async Task<bool> FileExistsAsync(string path)
        {
            await SimulateDelayAndErrors(path);
            return _files.ContainsKey(NormalizePath(path));
        }

        /// <summary>
        /// Gets all files in a directory
        /// </summary>
        public async Task<string[]> GetFilesAsync(string directoryPath, string searchPattern = "*")
        {
            await SimulateDelayAndErrors(directoryPath);
            
            var normalizedPath = NormalizePath(directoryPath);
            if (!_directories.ContainsKey(normalizedPath))
            {
                throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
            }

            var files = _files.Keys
                .Where(f => Path.GetDirectoryName(f)?.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) == true)
                .Where(f => MatchesPattern(Path.GetFileName(f), searchPattern))
                .ToArray();

            return files;
        }

        /// <summary>
        /// Gets all subdirectories in a directory
        /// </summary>
        public async Task<string[]> GetDirectoriesAsync(string directoryPath)
        {
            await SimulateDelayAndErrors(directoryPath);
            
            var normalizedPath = NormalizePath(directoryPath);
            if (!_directories.ContainsKey(normalizedPath))
            {
                throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
            }

            var subdirectories = _directories.Keys
                .Where(d => IsSubdirectoryOf(d, normalizedPath))
                .ToArray();

            return subdirectories;
        }

        /// <summary>
        /// Gets file information
        /// </summary>
        public async Task<FileInfo> GetFileInfoAsync(string filePath)
        {
            await SimulateDelayAndErrors(filePath);
            
            var normalizedPath = NormalizePath(filePath);
            if (!_files.TryGetValue(normalizedPath, out var mockFile))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            return new FileInfo(mockFile.Path)
            {
                // Note: FileInfo properties are read-only, so we'll use a custom approach
            };
        }

        /// <summary>
        /// Gets mock file metadata
        /// </summary>
        public async Task<FileMetadata> GetFileMetadataAsync(string filePath)
        {
            await SimulateDelayAndErrors(filePath);
            
            var normalizedPath = NormalizePath(filePath);
            if (!_files.TryGetValue(normalizedPath, out var mockFile))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            return new FileMetadata
            {
                FileName = Path.GetFileName(mockFile.Path),
                Size = mockFile.Content.Length,
                LastWriteTime = mockFile.LastWriteTime
            };
        }

        /// <summary>
        /// Reads file content as bytes
        /// </summary>
        public async Task<byte[]> ReadAllBytesAsync(string filePath)
        {
            await SimulateDelayAndErrors(filePath);
            
            var normalizedPath = NormalizePath(filePath);
            if (!_files.TryGetValue(normalizedPath, out var mockFile))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            return mockFile.Content;
        }

        /// <summary>
        /// Gets the size of a file
        /// </summary>
        public async Task<long> GetFileSizeAsync(string filePath)
        {
            var metadata = await GetFileMetadataAsync(filePath);
            return metadata.Size;
        }

        /// <summary>
        /// Clears all mock data
        /// </summary>
        public void Clear()
        {
            _directories.Clear();
            _files.Clear();
            _errorInjections.Clear();
            _delayInjections.Clear();
            
            // Re-add root
            _directories["C:\\"] = new MockDirectory("C:\\");
        }

        /// <summary>
        /// Gets all mock files for inspection
        /// </summary>
        public IReadOnlyDictionary<string, MockFile> GetAllFiles() => _files;

        /// <summary>
        /// Gets all mock directories for inspection
        /// </summary>
        public IReadOnlyDictionary<string, MockDirectory> GetAllDirectories() => _directories;

        private async Task SimulateDelayAndErrors(string path)
        {
            var normalizedPath = NormalizePath(path);
            
            // Check for injected errors
            if (_errorInjections.TryGetValue(normalizedPath, out var error))
            {
                throw error;
            }

            // Simulate delays
            if (_delayInjections.TryGetValue(normalizedPath, out var delay))
            {
                await Task.Delay(delay);
            }
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool MatchesPattern(string fileName, string pattern)
        {
            if (pattern == "*") return true;
            
            // Simple pattern matching - could be enhanced for more complex patterns
            if (pattern.StartsWith("*."))
            {
                var extension = pattern.Substring(1);
                return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
            }
            
            return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSubdirectoryOf(string path, string parentPath)
        {
            var parent = Path.GetDirectoryName(path);
            return parent?.Equals(parentPath, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    /// <summary>
    /// Represents a mock file in the file system
    /// </summary>
    public class MockFile
    {
        public string Path { get; }
        public byte[] Content { get; }
        public DateTime LastWriteTime { get; }
        public long Size => Content.Length;

        public MockFile(string path, byte[] content, DateTime lastWriteTime)
        {
            Path = path;
            Content = content;
            LastWriteTime = lastWriteTime;
        }
    }

    /// <summary>
    /// Represents a mock directory in the file system
    /// </summary>
    public class MockDirectory
    {
        public string Path { get; }
        public DateTime LastWriteTime { get; }

        public MockDirectory(string path, DateTime lastWriteTime = default)
        {
            Path = path;
            LastWriteTime = lastWriteTime == default ? DateTime.Now : lastWriteTime;
        }
    }
}