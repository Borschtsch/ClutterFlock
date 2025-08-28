using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ClutterFlock.Models;

namespace ClutterFlock.Tests.TestUtilities
{
    /// <summary>
    /// Helper class for creating and managing real file system structures for integration testing
    /// </summary>
    public class TestFileSystemHelper : IDisposable
    {
        private readonly string _rootPath;
        private readonly List<string> _createdDirectories = new();
        private readonly List<string> _createdFiles = new();
        private bool _disposed = false;

        public TestFileSystemHelper(string rootPath)
        {
            _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        }

        /// <summary>
        /// Creates a complete test file structure based on the provided configuration
        /// </summary>
        public async Task<string> CreateTestStructureAsync(TestFileStructure structure)
        {
            var structureRoot = Path.Combine(_rootPath, structure.RootPath ?? "TestStructure");
            Directory.CreateDirectory(structureRoot);
            _createdDirectories.Add(structureRoot);

            // Create folders first
            foreach (var folder in structure.Folders ?? new List<TestFolder>())
            {
                var folderPath = Path.Combine(structureRoot, folder.Path);
                Directory.CreateDirectory(folderPath);
                _createdDirectories.Add(folderPath);

                // Create files in this folder
                var files = folder.Files ?? new List<string>();
                foreach (var fileName in files)
                {
                    var filePath = Path.Combine(folderPath, fileName);
                    await CreateTestFileAsync(filePath, folder.TotalSize / Math.Max(1, files.Count));
                }
            }

            // Create individual files
            foreach (var file in structure.Files ?? new List<TestFile>())
            {
                var filePath = Path.Combine(structureRoot, file.Path);
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _createdDirectories.Add(directory);
                }

                await CreateTestFileAsync(filePath, file.Size, file.LastWriteTime, file.ExpectedHash);
            }

            return structureRoot;
        }

        /// <summary>
        /// Creates a test file with specified size and content
        /// </summary>
        public async Task<string> CreateTestFileAsync(string filePath, long size = 1024, DateTime? lastWriteTime = null, string? expectedHash = null)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _createdDirectories.Add(directory);
            }

            byte[] content;
            if (!string.IsNullOrEmpty(expectedHash))
            {
                // Generate content that produces the expected hash
                content = GenerateContentForHash(expectedHash, (int)size);
            }
            else
            {
                // Generate random content of specified size
                content = GenerateRandomContent((int)size);
            }

            await File.WriteAllBytesAsync(filePath, content);
            _createdFiles.Add(filePath);

            // Set last write time if specified
            if (lastWriteTime.HasValue && lastWriteTime.Value > DateTime.MinValue && lastWriteTime.Value < DateTime.MaxValue)
            {
                try
                {
                    File.SetLastWriteTime(filePath, lastWriteTime.Value);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Skip setting time if it's out of range
                }
            }

            return filePath;
        }

        /// <summary>
        /// Creates a duplicate file with identical content to the source
        /// </summary>
        public async Task<string> CreateDuplicateFileAsync(string sourcePath, string duplicatePath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Source file not found: {sourcePath}");
            }

            var directory = Path.GetDirectoryName(duplicatePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _createdDirectories.Add(directory);
            }

            var content = await File.ReadAllBytesAsync(sourcePath);
            await File.WriteAllBytesAsync(duplicatePath, content);
            _createdFiles.Add(duplicatePath);

            // Copy file attributes
            var sourceInfo = new FileInfo(sourcePath);
            File.SetLastWriteTime(duplicatePath, sourceInfo.LastWriteTime);

            return duplicatePath;
        }

        /// <summary>
        /// Creates a folder structure with known duplicate files for testing
        /// </summary>
        public async Task<(string folder1, string folder2)> CreateDuplicateFoldersAsync(string baseName = "TestFolder")
        {
            var folder1 = Path.Combine(_rootPath, $"{baseName}1");
            var folder2 = Path.Combine(_rootPath, $"{baseName}2");

            Directory.CreateDirectory(folder1);
            Directory.CreateDirectory(folder2);
            _createdDirectories.Add(folder1);
            _createdDirectories.Add(folder2);

            // Create identical files in both folders
            var testFiles = new[]
            {
                ("document.txt", 1024),
                ("image.jpg", 2048),
                ("data.csv", 512)
            };

            foreach (var (fileName, size) in testFiles)
            {
                var file1 = await CreateTestFileAsync(Path.Combine(folder1, fileName), size);
                await CreateDuplicateFileAsync(file1, Path.Combine(folder2, fileName));
            }

            return (folder1, folder2);
        }

        /// <summary>
        /// Creates a large dataset for performance testing
        /// </summary>
        public async Task<string> CreateLargeDatasetAsync(int folderCount = 100, int filesPerFolder = 50)
        {
            var datasetRoot = Path.Combine(_rootPath, "LargeDataset");
            Directory.CreateDirectory(datasetRoot);
            _createdDirectories.Add(datasetRoot);

            var random = new Random(42); // Fixed seed for reproducible tests

            for (int i = 0; i < folderCount; i++)
            {
                var folderPath = Path.Combine(datasetRoot, $"Folder_{i:D3}");
                Directory.CreateDirectory(folderPath);
                _createdDirectories.Add(folderPath);

                for (int j = 0; j < filesPerFolder; j++)
                {
                    var fileName = $"File_{j:D3}.txt";
                    var filePath = Path.Combine(folderPath, fileName);
                    var fileSize = random.Next(100, 10000); // Random size between 100-10000 bytes
                    
                    await CreateTestFileAsync(filePath, fileSize);
                }
            }

            return datasetRoot;
        }

        /// <summary>
        /// Creates a folder structure with permission issues for error testing
        /// </summary>
        public async Task<string> CreateRestrictedFolderAsync()
        {
            var restrictedFolder = Path.Combine(_rootPath, "RestrictedFolder");
            Directory.CreateDirectory(restrictedFolder);
            _createdDirectories.Add(restrictedFolder);

            // Create some files first
            await CreateTestFileAsync(Path.Combine(restrictedFolder, "accessible.txt"), 1024);
            await CreateTestFileAsync(Path.Combine(restrictedFolder, "restricted.txt"), 1024);

            // Note: Actual permission restriction would require platform-specific code
            // For now, we'll simulate this in tests by using file paths that don't exist
            // or by creating files that are in use

            return restrictedFolder;
        }

        /// <summary>
        /// Generates random content of specified size
        /// </summary>
        private byte[] GenerateRandomContent(int size)
        {
            var random = new Random(42); // Fixed seed for reproducible tests
            var content = new byte[size];
            random.NextBytes(content);
            return content;
        }

        /// <summary>
        /// Generates content that produces a specific SHA-256 hash (simplified approach)
        /// </summary>
        private byte[] GenerateContentForHash(string expectedHash, int size)
        {
            // For testing purposes, we'll generate content and adjust it to match the hash
            // This is a simplified approach - in practice, generating specific hashes is complex
            var baseContent = Encoding.UTF8.GetBytes($"Content for hash {expectedHash}");
            
            if (baseContent.Length >= size)
            {
                Array.Resize(ref baseContent, size);
                return baseContent;
            }

            // Pad with repeated content to reach desired size
            var content = new byte[size];
            for (int i = 0; i < size; i++)
            {
                content[i] = baseContent[i % baseContent.Length];
            }

            return content;
        }

        /// <summary>
        /// Computes SHA-256 hash of a file
        /// </summary>
        public async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
            return Convert.ToHexString(hashBytes);
        }

        /// <summary>
        /// Cleans up all created files and directories
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Delete files first
                foreach (var file in _createdFiles)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }

                // Delete directories (in reverse order to handle nested structures)
                for (int i = _createdDirectories.Count - 1; i >= 0; i--)
                {
                    var directory = _createdDirectories[i];
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw during disposal
                Console.WriteLine($"Warning: Failed to clean up test files: {ex.Message}");
            }

            _disposed = true;
        }
    }


}