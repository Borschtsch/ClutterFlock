using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Unit.Core
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ProjectManagerTests : TestBase
    {
        private ProjectManager _projectManager = null!;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _projectManager = new ProjectManager();
        }

        [TestMethod]
        public async Task SaveProjectAsync_WithValidData_SavesCorrectly()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "test.cfp");
            var projectData = new ProjectData
            {
                ScanFolders = new List<string> { @"C:\TestFolder" },
                CreatedDate = DateTime.Now,
                Version = "1.0"
            };

            // Act
            await _projectManager.SaveProjectAsync(tempFile, projectData);

            // Assert
            Assert.IsTrue(File.Exists(tempFile));
            
            var savedContent = await File.ReadAllTextAsync(tempFile);
            Assert.IsFalse(string.IsNullOrEmpty(savedContent));
            
            // Use the same deserialization approach as ProjectManager
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            
            var deserializedData = JsonSerializer.Deserialize<ProjectData>(savedContent, jsonOptions);
            Assert.IsNotNull(deserializedData);
            Assert.AreEqual("ClutterFlock", deserializedData.ApplicationName);
            Assert.IsNotNull(deserializedData.ScanFolders);
            Assert.AreEqual(1, deserializedData.ScanFolders.Count);
        }

        [TestMethod]
        public async Task SaveProjectAsync_SetsApplicationName()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "test.cfp");
            var projectData = new ProjectData
            {
                ScanFolders = new List<string> { @"C:\TestFolder" },
                ApplicationName = "SomeOtherApp" // This should be overridden
            };

            // Act
            await _projectManager.SaveProjectAsync(tempFile, projectData);

            // Assert
            var savedContent = await File.ReadAllTextAsync(tempFile);
            var deserializedData = JsonSerializer.Deserialize<ProjectData>(savedContent);
            Assert.IsNotNull(deserializedData);
            Assert.AreEqual("ClutterFlock", deserializedData.ApplicationName);
        }

        [TestMethod]
        public async Task SaveProjectAsync_WithInvalidPath_ThrowsException()
        {
            // Arrange
            var invalidPath = @"Z:\NonExistent\Path\test.cfp"; // Assuming Z: doesn't exist
            var projectData = TestDataGenerator.CreateSampleProjectData(new List<string> { @"C:\TestFolder" });

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await _projectManager.SaveProjectAsync(invalidPath, projectData));
            
            Assert.IsTrue(exception.Message.Contains("Failed to save project"));
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithValidFile_LoadsCorrectly()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "test.cfp");
            var originalData = TestDataGenerator.CreateSampleProjectData(new List<string> { @"C:\TestFolder1", @"C:\TestFolder2" });
            
            // Save the project first
            await _projectManager.SaveProjectAsync(tempFile, originalData);

            // Act
            var loadedData = await _projectManager.LoadProjectAsync(tempFile);

            // Assert
            Assert.IsNotNull(loadedData);
            Assert.AreEqual("ClutterFlock", loadedData.ApplicationName);
            Assert.AreEqual(originalData.ScanFolders.Count, loadedData.ScanFolders.Count);
            CollectionAssert.AreEqual(originalData.ScanFolders, loadedData.ScanFolders);
            // Note: The test data generator creates cache data, so we should have some
            Assert.IsTrue(loadedData.FileHashCache.Count > 0);
            Assert.IsTrue(loadedData.FolderInfoCache.Count > 0);
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithNonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".cfp");

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await _projectManager.LoadProjectAsync(nonExistentFile));
            
            Assert.IsTrue(exception.Message.Contains("Failed to load project"));
            Assert.IsInstanceOfType(exception.InnerException, typeof(FileNotFoundException));
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithInvalidJson_ThrowsException()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "invalid.cfp");
            await File.WriteAllTextAsync(tempFile, "{ invalid json content }");

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await _projectManager.LoadProjectAsync(tempFile));
            
            Assert.IsTrue(exception.Message.Contains("Failed to load project"));
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithEmptyFile_ThrowsException()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "empty.cfp");
            await File.WriteAllTextAsync(tempFile, "");

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await _projectManager.LoadProjectAsync(tempFile));
            
            Assert.IsTrue(exception.Message.Contains("Failed to load project"));
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithNullJsonContent_ThrowsException()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "null.cfp");
            await File.WriteAllTextAsync(tempFile, "null");

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await _projectManager.LoadProjectAsync(tempFile));
            
            Assert.IsTrue(exception.Message.Contains("Failed to load project"));
            Assert.IsInstanceOfType(exception.InnerException, typeof(InvalidDataException));
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithLegacyFile_SetsApplicationName()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "legacy.cfp");
            var legacyData = new ProjectData
            {
                ScanFolders = new List<string> { @"C:\TestFolder" },
                // ApplicationName is null/empty (legacy file)
            };
            
            var json = JsonSerializer.Serialize(legacyData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(tempFile, json);

            // Act
            var loadedData = await _projectManager.LoadProjectAsync(tempFile);

            // Assert
            Assert.IsNotNull(loadedData);
            Assert.AreEqual("ClutterFlock", loadedData.ApplicationName);
        }

        [TestMethod]
        public void IsValidProjectFile_WithValidCfpFile_ReturnsTrue()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "valid.cfp");
            var projectData = new ProjectData { ScanFolders = new List<string> { @"C:\TestFolder" } };
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(projectData, jsonOptions);
            File.WriteAllText(tempFile, json);

            // Act
            var result = _projectManager.IsValidProjectFile(tempFile);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsValidProjectFile_WithValidDfpFile_ReturnsTrue()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "valid.dfp");
            var projectData = new ProjectData { ScanFolders = new List<string> { @"C:\TestFolder" } };
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(projectData, jsonOptions);
            File.WriteAllText(tempFile, json);

            // Act
            var result = _projectManager.IsValidProjectFile(tempFile);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsValidProjectFile_WithInvalidExtension_ReturnsFalse()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "invalid.txt");
            var projectData = new ProjectData { ScanFolders = new List<string> { @"C:\TestFolder" } };
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(projectData, jsonOptions);
            File.WriteAllText(tempFile, json);

            // Act
            var result = _projectManager.IsValidProjectFile(tempFile);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsValidProjectFile_WithNonExistentFile_ReturnsFalse()
        {
            // Arrange
            var nonExistentFile = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".cfp");

            // Act
            var result = _projectManager.IsValidProjectFile(nonExistentFile);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsValidProjectFile_WithInvalidJson_ReturnsFalse()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "invalid.cfp");
            File.WriteAllText(tempFile, "{ invalid json }");

            // Act
            var result = _projectManager.IsValidProjectFile(tempFile);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsValidProjectFile_WithEmptyFile_ReturnsFalse()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "empty.cfp");
            File.WriteAllText(tempFile, "");

            // Act
            var result = _projectManager.IsValidProjectFile(tempFile);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task SaveAndLoadRoundTrip_PreservesAllData()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "roundtrip.cfp");
            var originalData = new ProjectData
            {
                ScanFolders = new List<string> { @"C:\Folder1", @"C:\Folder2", @"C:\Folder3" },
                CreatedDate = new DateTime(2023, 6, 15, 10, 30, 45),
                Version = "2.0",
                FileHashCache = new Dictionary<string, string>
                {
                    { @"C:\Folder1\file1.txt", "hash1" },
                    { @"C:\Folder1\file2.txt", "hash2" },
                    { @"C:\Folder2\file3.txt", "hash3" }
                },
                FolderFileCache = new Dictionary<string, List<string>>
                {
                    { @"C:\Folder1", new List<string> { @"C:\Folder1\file1.txt", @"C:\Folder1\file2.txt" } },
                    { @"C:\Folder2", new List<string> { @"C:\Folder2\file3.txt" } }
                },
                FolderInfoCache = new Dictionary<string, FolderInfo>
                {
                    { @"C:\Folder1", new FolderInfo { Files = new List<string> { @"C:\Folder1\file1.txt", @"C:\Folder1\file2.txt" }, TotalSize = 2048 } },
                    { @"C:\Folder2", new FolderInfo { Files = new List<string> { @"C:\Folder2\file3.txt" }, TotalSize = 1024 } }
                }
            };

            // Act
            await _projectManager.SaveProjectAsync(tempFile, originalData);
            var loadedData = await _projectManager.LoadProjectAsync(tempFile);

            // Assert
            Assert.IsNotNull(loadedData);
            Assert.AreEqual("ClutterFlock", loadedData.ApplicationName); // Should be set during save
            Assert.AreEqual(originalData.Version, loadedData.Version);
            Assert.AreEqual(originalData.CreatedDate, loadedData.CreatedDate);
            
            CollectionAssert.AreEqual(originalData.ScanFolders, loadedData.ScanFolders);
            
            Assert.AreEqual(originalData.FileHashCache.Count, loadedData.FileHashCache.Count);
            foreach (var kvp in originalData.FileHashCache)
            {
                Assert.IsTrue(loadedData.FileHashCache.ContainsKey(kvp.Key));
                Assert.AreEqual(kvp.Value, loadedData.FileHashCache[kvp.Key]);
            }
            
            Assert.AreEqual(originalData.FolderFileCache.Count, loadedData.FolderFileCache.Count);
            foreach (var kvp in originalData.FolderFileCache)
            {
                Assert.IsTrue(loadedData.FolderFileCache.ContainsKey(kvp.Key));
                CollectionAssert.AreEqual(kvp.Value, loadedData.FolderFileCache[kvp.Key]);
            }
            
            Assert.AreEqual(originalData.FolderInfoCache.Count, loadedData.FolderInfoCache.Count);
            foreach (var kvp in originalData.FolderInfoCache)
            {
                Assert.IsTrue(loadedData.FolderInfoCache.ContainsKey(kvp.Key));
                var originalInfo = kvp.Value;
                var loadedInfo = loadedData.FolderInfoCache[kvp.Key];
                Assert.AreEqual(originalInfo.TotalSize, loadedInfo.TotalSize);
                CollectionAssert.AreEqual(originalInfo.Files, loadedInfo.Files);
            }
        }

        [TestMethod]
        public async Task SaveProjectAsync_WithComplexData_HandlesLargeDatasets()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "large.cfp");
            var projectData = new ProjectData
            {
                ScanFolders = new List<string>()
            };

            // Create a large dataset
            for (int i = 0; i < 100; i++)
            {
                var folderPath = $@"C:\TestFolder{i:D3}";
                projectData.ScanFolders.Add(folderPath);
                
                var files = new List<string>();
                for (int j = 0; j < 50; j++)
                {
                    var filePath = $@"{folderPath}\file{j:D3}.txt";
                    files.Add(filePath);
                    projectData.FileHashCache[filePath] = $"hash_{i}_{j}";
                }
                
                projectData.FolderFileCache[folderPath] = files;
                projectData.FolderInfoCache[folderPath] = new FolderInfo
                {
                    Files = files,
                    TotalSize = files.Count * 1024
                };
            }

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _projectManager.SaveProjectAsync(tempFile, projectData);
            stopwatch.Stop();

            // Assert
            Assert.IsTrue(File.Exists(tempFile));
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 5000, "Save should complete within 5 seconds");
            
            var fileInfo = new FileInfo(tempFile);
            Assert.IsTrue(fileInfo.Length > 0, "File should not be empty");
            
            // Verify it can be loaded back
            var loadedData = await _projectManager.LoadProjectAsync(tempFile);
            Assert.AreEqual(projectData.ScanFolders.Count, loadedData.ScanFolders.Count);
            Assert.AreEqual(projectData.FileHashCache.Count, loadedData.FileHashCache.Count);
        }

        [TestMethod]
        public async Task LoadProjectAsync_WithCorruptedData_HandlesGracefully()
        {
            // Arrange
            var tempFile = Path.Combine(CreateTempDirectory(), "corrupted.cfp");
            
            // Create a JSON file with missing required fields
            var corruptedJson = @"{
                ""scanFolders"": null,
                ""fileHashCache"": ""invalid_type"",
                ""version"": 123
            }";
            await File.WriteAllTextAsync(tempFile, corruptedJson);

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await _projectManager.LoadProjectAsync(tempFile));
            
            Assert.IsTrue(exception.Message.Contains("Failed to load project"));
        }
    }
}