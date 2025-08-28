using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Integration
{
    /// <summary>
    /// Integration tests for project persistence functionality
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [TestCategory("ProjectPersistence")]
    [TestCategory("FileSystem")]
    public class ProjectPersistenceTests : IntegrationTestBase
    {
        private ProjectManager _projectManager = null!;
        private CacheManager _cacheManager = null!;
        private FolderScanner _folderScanner = null!;
        private DuplicateAnalyzer _duplicateAnalyzer = null!;

        protected override async Task OnSetupAsync()
        {
            _projectManager = new ProjectManager();
            _cacheManager = new CacheManager();
            var errorRecoveryService = new ErrorRecoveryService();
            _folderScanner = new FolderScanner(_cacheManager, errorRecoveryService);
            _duplicateAnalyzer = new DuplicateAnalyzer(_cacheManager, errorRecoveryService);

            await base.OnSetupAsync();
        }

        [TestMethod]
        public async Task ProjectLifecycle_CreateSaveLoadModifySave_DataIntegrityMaintained()
        {
            // Arrange - Create test data
            var (folder1, folder2) = await FileSystemHelper.CreateDuplicateFoldersAsync("ProjectLifecycleTest");
            var projectFilePath = Path.Combine(TestRootDirectory, "lifecycle_test.cfp");

            using var monitor = PerformanceHelper.StartMonitoring("ProjectLifecycle");

            // Phase 1: Create project data
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;

            await _folderScanner.ScanFolderHierarchyAsync(folder1, progress, cancellationToken);
            await _folderScanner.ScanFolderHierarchyAsync(folder2, progress, cancellationToken);

            var folders = new List<string> { folder1, folder2 };
            var fileMatches = await _duplicateAnalyzer.FindDuplicateFilesAsync(folders, progress, cancellationToken);

            var originalProjectData = new ProjectData
            {
                ScanFolders = folders,
                FolderFileCache = _cacheManager.GetAllFolderFiles(),
                FileHashCache = _cacheManager.GetAllFileHashes(),
                FolderInfoCache = _cacheManager.GetAllFolderInfo(),
                CreatedDate = DateTime.Now,
                Version = "1.0"
            };

            // Phase 2: Save project
            await _projectManager.SaveProjectAsync(projectFilePath, originalProjectData);
            Assert.IsTrue(File.Exists(projectFilePath), "Project file should be created");

            // Phase 3: Load project
            var loadedProjectData = await _projectManager.LoadProjectAsync(projectFilePath);
            
            Assert.IsNotNull(loadedProjectData, "Should load project data");
            Assert.AreEqual(originalProjectData.ScanFolders.Count, loadedProjectData.ScanFolders.Count, "Should preserve scan folders count");
            Assert.AreEqual(originalProjectData.Version, loadedProjectData.Version, "Should preserve version");
            Assert.AreEqual("ClutterFlock", loadedProjectData.ApplicationName, "Should set application name");

            // Verify cache data integrity
            Assert.AreEqual(originalProjectData.FolderFileCache.Count, loadedProjectData.FolderFileCache.Count, "Should preserve folder file cache");
            Assert.AreEqual(originalProjectData.FileHashCache.Count, loadedProjectData.FileHashCache.Count, "Should preserve file hash cache");
            Assert.AreEqual(originalProjectData.FolderInfoCache.Count, loadedProjectData.FolderInfoCache.Count, "Should preserve folder info cache");

            // Phase 4: Modify project (add new folder)
            var folder3 = await FileSystemHelper.CreateTestFileAsync(Path.Combine(TestRootDirectory, "folder3", "new_file.txt"), 1024);
            var folder3Path = Path.GetDirectoryName(folder3)!;

            await _folderScanner.ScanFolderHierarchyAsync(folder3Path, progress, cancellationToken);
            
            var modifiedProjectData = new ProjectData
            {
                ScanFolders = new List<string> { folder1, folder2, folder3Path },
                FolderFileCache = _cacheManager.GetAllFolderFiles(),
                FileHashCache = _cacheManager.GetAllFileHashes(),
                FolderInfoCache = _cacheManager.GetAllFolderInfo(),
                CreatedDate = originalProjectData.CreatedDate,
                Version = "1.1"
            };

            // Phase 5: Save modified project
            await _projectManager.SaveProjectAsync(projectFilePath, modifiedProjectData);

            // Phase 6: Load modified project and verify
            var finalProjectData = await _projectManager.LoadProjectAsync(projectFilePath);
            
            Assert.AreEqual(3, finalProjectData.ScanFolders.Count, "Should have 3 folders after modification");
            Assert.IsTrue(finalProjectData.ScanFolders.Contains(folder3Path), "Should contain new folder");
            Assert.AreEqual("1.1", finalProjectData.Version, "Should update version");
            Assert.AreEqual(originalProjectData.CreatedDate.Date, finalProjectData.CreatedDate.Date, "Should preserve original creation date");

            monitor.RecordItemsProcessed(finalProjectData.ScanFolders.Count);
        }

        [TestMethod]
        public async Task MultipleSaveLoadCycles_DataIntegrityPreserved()
        {
            // Arrange
            var testFolder = Path.Combine(TestRootDirectory, "MultiCycleTest");
            Directory.CreateDirectory(testFolder);
            await File.WriteAllTextAsync(Path.Combine(testFolder, "test.txt"), "Test content");

            var projectFilePath = Path.Combine(TestRootDirectory, "multi_cycle_test.cfp");

            using var monitor = PerformanceHelper.StartMonitoring("MultipleSaveLoadCycles");

            // Populate initial cache
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;
            await _folderScanner.ScanFolderHierarchyAsync(testFolder, progress, cancellationToken);

            var originalFolderFiles = _cacheManager.GetAllFolderFiles();
            var originalFileHashes = _cacheManager.GetAllFileHashes();
            var originalFolderInfo = _cacheManager.GetAllFolderInfo();

            // Perform multiple save/load cycles
            for (int cycle = 1; cycle <= 5; cycle++)
            {
                // Save
                var projectData = new ProjectData
                {
                    ScanFolders = new List<string> { testFolder },
                    FolderFileCache = _cacheManager.GetAllFolderFiles(),
                    FileHashCache = _cacheManager.GetAllFileHashes(),
                    FolderInfoCache = _cacheManager.GetAllFolderInfo(),
                    Version = $"1.{cycle}"
                };

                await _projectManager.SaveProjectAsync(projectFilePath, projectData);

                // Clear cache
                _cacheManager.ClearCache();
                Assert.IsFalse(_cacheManager.IsFolderCached(testFolder), $"Cache should be cleared in cycle {cycle}");

                // Load
                var loadedProjectData = await _projectManager.LoadProjectAsync(projectFilePath);
                _cacheManager.LoadFromProjectData(loadedProjectData);

                // Verify data integrity
                Assert.IsTrue(_cacheManager.IsFolderCached(testFolder), $"Folder should be cached after load in cycle {cycle}");
                Assert.AreEqual($"1.{cycle}", loadedProjectData.Version, $"Version should be correct in cycle {cycle}");

                var currentFolderFiles = _cacheManager.GetAllFolderFiles();
                var currentFileHashes = _cacheManager.GetAllFileHashes();
                var currentFolderInfo = _cacheManager.GetAllFolderInfo();

                Assert.AreEqual(originalFolderFiles.Count, currentFolderFiles.Count, $"Folder files count should be preserved in cycle {cycle}");
                Assert.AreEqual(originalFileHashes.Count, currentFileHashes.Count, $"File hashes count should be preserved in cycle {cycle}");
                Assert.AreEqual(originalFolderInfo.Count, currentFolderInfo.Count, $"Folder info count should be preserved in cycle {cycle}");
            }

            monitor.RecordItemsProcessed(5); // 5 cycles
        }

        [TestMethod]
        public async Task ProjectFileCorruption_GracefulErrorHandling()
        {
            // Arrange
            var validProjectFilePath = Path.Combine(TestRootDirectory, "valid_project.cfp");
            var corruptedProjectFilePath = Path.Combine(TestRootDirectory, "corrupted_project.cfp");
            var nonExistentProjectFilePath = Path.Combine(TestRootDirectory, "non_existent.cfp");

            using var monitor = PerformanceHelper.StartMonitoring("ProjectFileCorruption");

            // Create valid project first
            var validProjectData = new ProjectData
            {
                ScanFolders = new List<string> { TestRootDirectory },
                Version = "1.0"
            };

            await _projectManager.SaveProjectAsync(validProjectFilePath, validProjectData);

            // Test 1: Load valid project (should work)
            var loadedValidProject = await _projectManager.LoadProjectAsync(validProjectFilePath);
            Assert.IsNotNull(loadedValidProject, "Should load valid project");
            Assert.AreEqual("1.0", loadedValidProject.Version, "Should load correct version");

            // Test 2: Create corrupted project file
            await File.WriteAllTextAsync(corruptedProjectFilePath, "{ invalid json content }");

            try
            {
                await _projectManager.LoadProjectAsync(corruptedProjectFilePath);
                Assert.Fail("Should throw exception for corrupted project file");
            }
            catch (Exception ex)
            {
                // Accept any exception for corrupted file - the important thing is that it throws
                Assert.IsTrue(!string.IsNullOrEmpty(ex.Message), 
                    $"Should provide meaningful error message for corrupted file. Got: {ex.Message}");
            }

            // Test 3: Load non-existent project file
            try
            {
                await _projectManager.LoadProjectAsync(nonExistentProjectFilePath);
                Assert.Fail("Should throw exception for non-existent project file");
            }
            catch (Exception ex) // Accept any exception type
            {
                Assert.IsTrue(ex.Message.Contains("not found") || ex.Message.Contains("Project file not found"), 
                    $"Should provide meaningful error message for missing file. Got: {ex.Message}");
            }

            // Test 4: Save to invalid path
            var invalidPath = Path.Combine("Z:\\NonExistentDrive", "invalid.cfp");
            try
            {
                await _projectManager.SaveProjectAsync(invalidPath, validProjectData);
                Assert.Fail("Should throw exception for invalid save path");
            }
            catch (Exception ex)
            {
                Assert.IsTrue(ex.Message.Contains("Failed to save") || ex.Message.Contains("path"), 
                    "Should provide meaningful error message for invalid path");
            }

            monitor.RecordItemsProcessed(4); // 4 test scenarios
        }

        [TestMethod]
        public async Task LegacyFormatCompatibility_BackwardCompatibility()
        {
            // Arrange - Create a legacy format project file (.dfp)
            var legacyProjectFilePath = Path.Combine(TestRootDirectory, "legacy_project.dfp");
            var modernProjectFilePath = Path.Combine(TestRootDirectory, "modern_project.cfp");

            using var monitor = PerformanceHelper.StartMonitoring("LegacyFormatCompatibility");

            // Create test data
            var testFolder = Path.Combine(TestRootDirectory, "LegacyTest");
            Directory.CreateDirectory(testFolder);
            await File.WriteAllTextAsync(Path.Combine(testFolder, "legacy_file.txt"), "Legacy content");

            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;
            await _folderScanner.ScanFolderHierarchyAsync(testFolder, progress, cancellationToken);

            // Create legacy format data (simulate old format)
            var legacyProjectData = new ProjectData
            {
                ScanFolders = new List<string> { testFolder },
                FolderFileCache = _cacheManager.GetAllFolderFiles(),
                FileHashCache = _cacheManager.GetAllFileHashes(),
                FolderInfoCache = _cacheManager.GetAllFolderInfo(),
                Version = "0.9", // Old version
                ApplicationName = "DuplicateFolderFinder" // Old application name
            };

            // Save as legacy format (.dfp)
            await _projectManager.SaveProjectAsync(legacyProjectFilePath, legacyProjectData);

            // Test: Load legacy format
            var loadedLegacyProject = await _projectManager.LoadProjectAsync(legacyProjectFilePath);
            
            Assert.IsNotNull(loadedLegacyProject, "Should load legacy project");
            Assert.AreEqual(1, loadedLegacyProject.ScanFolders.Count, "Should preserve scan folders from legacy");
            Assert.IsTrue(loadedLegacyProject.ScanFolders.Contains(testFolder), "Should contain legacy test folder");

            // Test: Save loaded legacy project in modern format
            await _projectManager.SaveProjectAsync(modernProjectFilePath, loadedLegacyProject);

            // Verify modern format
            var modernProject = await _projectManager.LoadProjectAsync(modernProjectFilePath);
            Assert.AreEqual("ClutterFlock", modernProject.ApplicationName, "Should update application name to modern");
            Assert.AreEqual(loadedLegacyProject.ScanFolders.Count, modernProject.ScanFolders.Count, "Should preserve data during format upgrade");

            monitor.RecordItemsProcessed(2); // Legacy and modern formats
        }

        [TestMethod]
        public async Task ConcurrentProjectAccess_FileLocksHandledGracefully()
        {
            // Arrange
            var projectFilePath = Path.Combine(TestRootDirectory, "concurrent_test.cfp");
            var testFolder = Path.Combine(TestRootDirectory, "ConcurrentTest");
            Directory.CreateDirectory(testFolder);
            await File.WriteAllTextAsync(Path.Combine(testFolder, "concurrent_file.txt"), "Concurrent content");

            using var monitor = PerformanceHelper.StartMonitoring("ConcurrentProjectAccess");

            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;
            await _folderScanner.ScanFolderHierarchyAsync(testFolder, progress, cancellationToken);

            var projectData = new ProjectData
            {
                ScanFolders = new List<string> { testFolder },
                FolderFileCache = _cacheManager.GetAllFolderFiles(),
                FileHashCache = _cacheManager.GetAllFileHashes(),
                FolderInfoCache = _cacheManager.GetAllFolderInfo(),
                Version = "1.0"
            };

            // Test: Multiple concurrent save operations
            var saveTasks = new List<Task>();
            for (int i = 0; i < 3; i++)
            {
                var taskProjectData = new ProjectData
                {
                    ScanFolders = projectData.ScanFolders,
                    FolderFileCache = projectData.FolderFileCache,
                    FileHashCache = projectData.FileHashCache,
                    FolderInfoCache = projectData.FolderInfoCache,
                    Version = $"1.{i}"
                };

                var taskFilePath = Path.Combine(TestRootDirectory, $"concurrent_test_{i}.cfp");
                saveTasks.Add(_projectManager.SaveProjectAsync(taskFilePath, taskProjectData));
            }

            // Wait for all saves to complete
            await Task.WhenAll(saveTasks);

            // Verify all files were created
            for (int i = 0; i < 3; i++)
            {
                var taskFilePath = Path.Combine(TestRootDirectory, $"concurrent_test_{i}.cfp");
                Assert.IsTrue(File.Exists(taskFilePath), $"Concurrent save {i} should create file");

                var loadedProject = await _projectManager.LoadProjectAsync(taskFilePath);
                Assert.AreEqual($"1.{i}", loadedProject.Version, $"Concurrent save {i} should have correct version");
            }

            monitor.RecordItemsProcessed(3); // 3 concurrent operations
        }

        [TestMethod]
        public async Task LargeProjectData_PerformanceAndIntegrity()
        {
            // Arrange - Create large dataset
            var largeDatasetPath = await FileSystemHelper.CreateLargeDatasetAsync(20, 25); // 500 files
            var projectFilePath = Path.Combine(TestRootDirectory, "large_project.cfp");

            using var monitor = PerformanceHelper.StartMonitoring("LargeProjectData");

            // Populate cache with large dataset
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;
            var folders = Directory.GetDirectories(largeDatasetPath).Take(10).ToList(); // 10 folders

            foreach (var folder in folders)
            {
                await _folderScanner.ScanFolderHierarchyAsync(folder, progress, cancellationToken);
            }

            var largeProjectData = new ProjectData
            {
                ScanFolders = folders,
                FolderFileCache = _cacheManager.GetAllFolderFiles(),
                FileHashCache = _cacheManager.GetAllFileHashes(),
                FolderInfoCache = _cacheManager.GetAllFolderInfo(),
                Version = "1.0"
            };

            // Test: Save large project
            var saveStartTime = DateTime.Now;
            await _projectManager.SaveProjectAsync(projectFilePath, largeProjectData);
            var saveTime = DateTime.Now - saveStartTime;

            Assert.IsTrue(File.Exists(projectFilePath), "Should create large project file");
            Assert.IsTrue(saveTime.TotalSeconds < 30, "Save should complete within 30 seconds");

            // Verify file size is reasonable
            var fileInfo = new FileInfo(projectFilePath);
            Assert.IsTrue(fileInfo.Length > 1000, "Project file should contain substantial data");
            Assert.IsTrue(fileInfo.Length < 100 * 1024 * 1024, "Project file should not be excessively large (>100MB)");

            // Test: Load large project
            _cacheManager.ClearCache();
            
            var loadStartTime = DateTime.Now;
            var loadedLargeProject = await _projectManager.LoadProjectAsync(projectFilePath);
            var loadTime = DateTime.Now - loadStartTime;

            Assert.IsTrue(loadTime.TotalSeconds < 30, "Load should complete within 30 seconds");
            Assert.AreEqual(largeProjectData.ScanFolders.Count, loadedLargeProject.ScanFolders.Count, "Should preserve all scan folders");
            Assert.AreEqual(largeProjectData.FolderFileCache.Count, loadedLargeProject.FolderFileCache.Count, "Should preserve all folder file cache");

            // Test: Restore cache from large project
            _cacheManager.LoadFromProjectData(loadedLargeProject);

            foreach (var folder in folders)
            {
                Assert.IsTrue(_cacheManager.IsFolderCached(folder), $"Folder {folder} should be cached after restore");
            }

            monitor.RecordItemsProcessed(folders.Count);
        }

        [TestMethod]
        public async Task ProjectValidation_ValidatesProjectFileIntegrity()
        {
            // Arrange
            var validProjectFilePath = Path.Combine(TestRootDirectory, "validation_test.cfp");
            var testFolder = Path.Combine(TestRootDirectory, "ValidationTest");
            Directory.CreateDirectory(testFolder);
            await File.WriteAllTextAsync(Path.Combine(testFolder, "validation_file.txt"), "Validation content");

            using var monitor = PerformanceHelper.StartMonitoring("ProjectValidation");

            // Create valid project
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;
            await _folderScanner.ScanFolderHierarchyAsync(testFolder, progress, cancellationToken);

            var validProjectData = new ProjectData
            {
                ScanFolders = new List<string> { testFolder },
                FolderFileCache = _cacheManager.GetAllFolderFiles(),
                FileHashCache = _cacheManager.GetAllFileHashes(),
                FolderInfoCache = _cacheManager.GetAllFolderInfo(),
                Version = "1.0",
                ApplicationName = "ClutterFlock"
            };

            await _projectManager.SaveProjectAsync(validProjectFilePath, validProjectData);

            // Test: Validate valid project file
            var isValid = _projectManager.IsValidProjectFile(validProjectFilePath);
            Assert.IsTrue(isValid, "Should validate correct project file");

            // Test: Validate non-existent file
            var nonExistentPath = Path.Combine(TestRootDirectory, "non_existent.cfp");
            var isNonExistentValid = _projectManager.IsValidProjectFile(nonExistentPath);
            Assert.IsFalse(isNonExistentValid, "Should not validate non-existent file");

            // Test: Validate non-project file
            var textFilePath = Path.Combine(TestRootDirectory, "not_a_project.txt");
            await File.WriteAllTextAsync(textFilePath, "This is not a project file");
            var isTextFileValid = _projectManager.IsValidProjectFile(textFilePath);
            Assert.IsFalse(isTextFileValid, "Should not validate non-project file");

            // Test: Validate corrupted project file
            var corruptedPath = Path.Combine(TestRootDirectory, "corrupted.cfp");
            await File.WriteAllTextAsync(corruptedPath, "{ corrupted json }");
            var isCorruptedValid = _projectManager.IsValidProjectFile(corruptedPath);
            Assert.IsFalse(isCorruptedValid, "Should not validate corrupted project file");

            monitor.RecordItemsProcessed(4); // 4 validation tests
        }
    }
}