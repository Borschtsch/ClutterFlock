using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClutterFlock.Models;

namespace ClutterFlock.Tests.TestUtilities
{
    /// <summary>
    /// Utility for generating test data structures and sample files for testing
    /// </summary>
    public class TestDataGenerator
    {
        private readonly Random _random = new();
        private static readonly string[] CommonFileExtensions = { ".txt", ".jpg", ".png", ".pdf", ".doc", ".mp3", ".mp4", ".zip" };
        private static readonly string[] CommonFileNames = { "document", "photo", "image", "file", "data", "backup", "temp", "report" };

        /// <summary>
        /// Creates a test file structure with known duplicate patterns
        /// </summary>
        public TestFileStructure CreateTestStructure(string rootPath, int folderCount = 5, int filesPerFolder = 10)
        {
            var structure = new TestFileStructure { RootPath = rootPath };
            
            for (int i = 0; i < folderCount; i++)
            {
                var folderPath = Path.Combine(rootPath, $"Folder{i:D2}");
                var folder = CreateTestFolder(folderPath, filesPerFolder);
                structure.Folders.Add(folder);
                structure.Files.AddRange(folder.Files.Select(f => new TestFile
                {
                    Path = f,
                    Name = Path.GetFileName(f),
                    Size = _random.Next(1024, 1024 * 1024), // 1KB to 1MB
                    LastWriteTime = DateTime.Now.AddDays(-_random.Next(1, 365)),
                    ExpectedHash = GenerateTestHash(f)
                }));
            }

            return structure;
        }

        /// <summary>
        /// Creates a test structure with specific duplicate patterns
        /// </summary>
        public TestFileStructure CreateDuplicateTestStructure(string rootPath)
        {
            var structure = new TestFileStructure { RootPath = rootPath };

            // Create folders with known duplicate patterns
            var folder1 = CreateFolderWithFiles(Path.Combine(rootPath, "Photos_2023"), new[]
            {
                ("vacation1.jpg", 2048000L, "hash1"),
                ("vacation2.jpg", 1536000L, "hash2"),
                ("family.jpg", 3072000L, "hash3"),
                ("unique1.jpg", 1024000L, "hash4")
            });

            var folder2 = CreateFolderWithFiles(Path.Combine(rootPath, "Photos_Backup"), new[]
            {
                ("vacation1.jpg", 2048000L, "hash1"), // Duplicate
                ("vacation2.jpg", 1536000L, "hash2"), // Duplicate
                ("family_copy.jpg", 3072000L, "hash3"), // Duplicate (different name)
                ("unique2.jpg", 2048000L, "hash5")
            });

            var folder3 = CreateFolderWithFiles(Path.Combine(rootPath, "Documents"), new[]
            {
                ("report.pdf", 512000L, "hash6"),
                ("presentation.pptx", 1024000L, "hash7"),
                ("notes.txt", 4096L, "hash8")
            });

            structure.Folders.AddRange(new[] { folder1, folder2, folder3 });
            
            // Add all files to the structure
            foreach (var folder in structure.Folders)
            {
                structure.Files.AddRange(folder.Files.Select(f => new TestFile
                {
                    Path = f,
                    Name = Path.GetFileName(f),
                    Size = GetFileSizeFromPath(f, folder),
                    LastWriteTime = DateTime.Now.AddDays(-_random.Next(1, 30)),
                    ExpectedHash = GetFileHashFromPath(f, folder)
                }));
            }

            return structure;
        }

        /// <summary>
        /// Creates a large test structure for performance testing
        /// </summary>
        public TestFileStructure CreateLargeTestStructure(string rootPath, int folderCount = 100, int filesPerFolder = 100)
        {
            var structure = new TestFileStructure { RootPath = rootPath };
            
            for (int i = 0; i < folderCount; i++)
            {
                var folderPath = Path.Combine(rootPath, $"LargeFolder{i:D3}");
                var folder = CreateTestFolder(folderPath, filesPerFolder);
                structure.Folders.Add(folder);
                
                // Add files with some duplicates across folders
                for (int j = 0; j < filesPerFolder; j++)
                {
                    var fileName = $"file{j:D3}{GetRandomExtension()}";
                    var filePath = Path.Combine(folderPath, fileName);
                    
                    // Create some duplicates by reusing hashes
                    var hash = (i > 0 && j < 10) ? $"duplicate_hash_{j}" : GenerateTestHash(filePath);
                    
                    structure.Files.Add(new TestFile
                    {
                        Path = filePath,
                        Name = fileName,
                        Size = _random.Next(1024, 10 * 1024 * 1024), // 1KB to 10MB
                        LastWriteTime = DateTime.Now.AddDays(-_random.Next(1, 1000)),
                        ExpectedHash = hash
                    });
                }
            }

            return structure;
        }

        /// <summary>
        /// Creates a mock file system populated with test data
        /// </summary>
        public MockFileSystem CreateMockFileSystem(TestFileStructure structure)
        {
            var mockFs = new MockFileSystem();

            // Create directories
            foreach (var folder in structure.Folders)
            {
                mockFs.CreateDirectory(folder.Path, folder.LatestModification);
            }

            // Create files
            foreach (var file in structure.Files)
            {
                var content = GenerateFileContent(file.Size);
                mockFs.CreateFile(file.Path, content, file.LastWriteTime);
            }

            return mockFs;
        }

        /// <summary>
        /// Creates temporary real directories and files for integration testing
        /// </summary>
        public string CreateTempTestStructure(TestFileStructure structure)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "ClutterFlockTest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempRoot);

            try
            {
                // Create directories
                foreach (var folder in structure.Folders)
                {
                    var actualPath = folder.Path.Replace(structure.RootPath, tempRoot);
                    Directory.CreateDirectory(actualPath);
                }

                // Create files
                foreach (var file in structure.Files)
                {
                    var actualPath = file.Path.Replace(structure.RootPath, tempRoot);
                    var content = GenerateFileContent(file.Size);
                    File.WriteAllBytes(actualPath, content);
                    File.SetLastWriteTime(actualPath, file.LastWriteTime);
                }

                return tempRoot;
            }
            catch
            {
                // Clean up on error
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
                throw;
            }
        }

        /// <summary>
        /// Cleans up temporary test directories
        /// </summary>
        public static void CleanupTempDirectory(string tempPath)
        {
            if (Directory.Exists(tempPath))
            {
                try
                {
                    Directory.Delete(tempPath, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }

        /// <summary>
        /// Generates sample project data for testing
        /// </summary>
        public ProjectData CreateSampleProjectData(List<string> scanFolders)
        {
            var projectData = new ProjectData
            {
                ScanFolders = scanFolders,
                CreatedDate = DateTime.Now,
                Version = "1.0",
                ApplicationName = "ClutterFlock"
            };

            // Add some sample cache data
            foreach (var folder in scanFolders)
            {
                var files = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    var fileName = $"sample{i}.txt";
                    var filePath = Path.Combine(folder, fileName);
                    files.Add(filePath);
                    
                    projectData.FileHashCache[filePath] = GenerateTestHash(filePath);
                }
                
                projectData.FolderFileCache[folder] = files;
                projectData.FolderInfoCache[folder] = new FolderInfo
                {
                    Files = files,
                    TotalSize = files.Count * 1024,
                    LatestModificationDate = DateTime.Now.AddDays(-_random.Next(1, 30))
                };
            }

            return projectData;
        }

        private TestFolder CreateTestFolder(string folderPath, int fileCount)
        {
            var files = new List<string>();
            long totalSize = 0;
            DateTime latestModification = DateTime.MinValue;

            for (int i = 0; i < fileCount; i++)
            {
                var fileName = $"{GetRandomFileName()}{GetRandomExtension()}";
                var filePath = Path.Combine(folderPath, fileName);
                files.Add(filePath);
                
                var fileSize = _random.Next(1024, 1024 * 1024);
                totalSize += fileSize;
                
                var fileDate = DateTime.Now.AddDays(-_random.Next(1, 365));
                if (fileDate > latestModification)
                    latestModification = fileDate;
            }

            return new TestFolder
            {
                Path = folderPath,
                Files = files,
                TotalSize = totalSize,
                LatestModification = latestModification
            };
        }

        private TestFolder CreateFolderWithFiles(string folderPath, (string name, long size, string hash)[] files)
        {
            var filePaths = new List<string>();
            long totalSize = 0;

            foreach (var (name, size, hash) in files)
            {
                filePaths.Add(Path.Combine(folderPath, name));
                totalSize += size;
            }

            return new TestFolder
            {
                Path = folderPath,
                Files = filePaths,
                TotalSize = totalSize,
                LatestModification = DateTime.Now.AddDays(-_random.Next(1, 30))
            };
        }

        private long GetFileSizeFromPath(string filePath, TestFolder folder)
        {
            // This is a simplified approach - in a real implementation,
            // you'd store this information more systematically
            return _random.Next(1024, 5 * 1024 * 1024);
        }

        private string GetFileHashFromPath(string filePath, TestFolder folder)
        {
            // Return a consistent hash for the same file path
            return GenerateTestHash(filePath);
        }

        private string GetRandomFileName()
        {
            return CommonFileNames[_random.Next(CommonFileNames.Length)] + _random.Next(1, 100);
        }

        private string GetRandomExtension()
        {
            return CommonFileExtensions[_random.Next(CommonFileExtensions.Length)];
        }

        private string GenerateTestHash(string input)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash);
        }

        private byte[] GenerateFileContent(long size)
        {
            var content = new byte[size];
            _random.NextBytes(content);
            return content;
        }
    }

    /// <summary>
    /// Represents a test file structure
    /// </summary>
    public class TestFileStructure
    {
        public string RootPath { get; set; } = string.Empty;
        public List<TestFolder> Folders { get; set; } = new();
        public List<TestFile> Files { get; set; } = new();
    }

    /// <summary>
    /// Represents a test folder
    /// </summary>
    public class TestFolder
    {
        public string Path { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new();
        public long TotalSize { get; set; }
        public DateTime? LatestModification { get; set; }
    }

    /// <summary>
    /// Represents a test file
    /// </summary>
    public class TestFile
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTime { get; set; }
        public string ExpectedHash { get; set; } = string.Empty;
    }
}