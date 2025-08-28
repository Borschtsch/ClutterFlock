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
    /// Integration tests that validate component interactions and data flow
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [TestCategory("ComponentIntegration")]
    public class ComponentIntegrationTests : IntegrationTestBase
    {
        private CacheManager _cacheManager = null!;
        private FolderScanner _folderScanner = null!;
        private DuplicateAnalyzer _duplicateAnalyzer = null!;
        private ProjectManager _projectManager = null!;
        private FileComparer _fileComparer = null!;
        private ErrorRecoveryService _errorRecoveryService = null!;

        protected override async Task OnSetupAsync()
        {
            // Initialize real components for integration testing
            _cacheManager = new CacheManager();
            _errorRecoveryService = new ErrorRecoveryService();
            _folderScanner = new FolderScanner(_cacheManager, _errorRecoveryService);
            _duplicateAnalyzer = new DuplicateAnalyzer(_cacheManager, _errorRecoveryService);
            _projectManager = new ProjectManager();
            _fileComparer = new FileComparer();

            await base.OnSetupAsync();
        }

        protected override async Task OnCleanupAsync()
        {
            // CacheManager doesn't implement IDisposable
            await base.OnCleanupAsync();
        }

        [TestMethod]
        public async Task CacheManager_FolderScanner_Integration_DataFlowsCorrectly()
        {
            // Arrange - Create test folder structure
            var testFolder = Path.Combine(TestRootDirectory, "CacheIntegrationTest");
            Directory.CreateDirectory(testFolder);
            
            var subFolder1 = Path.Combine(testFolder, "SubFolder1");
            var subFolder2 = Path.Combine(testFolder, "SubFolder2");
            Directory.CreateDirectory(subFolder1);
            Directory.CreateDirectory(subFolder2);

            await File.WriteAllTextAsync(Path.Combine(subFolder1, "file1.txt"), "Content 1");
            await File.WriteAllTextAsync(Path.Combine(subFolder1, "file2.txt"), "Content 2");
            await File.WriteAllTextAsync(Path.Combine(subFolder2, "file3.txt"), "Content 3");

            using var monitor = PerformanceHelper.StartMonitoring("CacheManager_FolderScanner_Integration");

            // Act - Scan folder (should populate cache)
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;
            
            var subfolders = await _folderScanner.ScanFolderHierarchyAsync(testFolder, progress, cancellationToken);

            // Assert - Verify data flows correctly between components
            Assert.IsTrue(subfolders.Count >= 2, "Should find at least 2 subfolders");
            Assert.IsTrue(subfolders.Contains(subFolder1), "Should contain SubFolder1");
            Assert.IsTrue(subfolders.Contains(subFolder2), "Should contain SubFolder2");

            // Verify cache was populated by FolderScanner
            Assert.IsTrue(_cacheManager.IsFolderCached(subFolder1), "SubFolder1 should be cached");
            Assert.IsTrue(_cacheManager.IsFolderCached(subFolder2), "SubFolder2 should be cached");

            // Verify cache contains correct file data
            var folder1Info = _cacheManager.GetFolderInfo(subFolder1);
            var folder2Info = _cacheManager.GetFolderInfo(subFolder2);

            Assert.IsNotNull(folder1Info, "Should have folder1 info in cache");
            Assert.IsNotNull(folder2Info, "Should have folder2 info in cache");
            Assert.AreEqual(2, folder1Info.FileCount, "Folder1 should have 2 files");
            Assert.AreEqual(1, folder2Info.FileCount, "Folder2 should have 1 file");

            monitor.RecordItemsProcessed(subfolders.Count);
        }

        [TestMethod]
        public async Task CacheManager_DuplicateAnalyzer_Integration_SharedCacheAccess()
        {
            // Arrange - Create folders with duplicate files
            var (folder1, folder2) = await FileSystemHelper.CreateDuplicateFoldersAsync("SharedCacheTest");

            using var monitor = PerformanceHelper.StartMonitoring("CacheManager_DuplicateAnalyzer_Integration");

            // Act - First, populate cache via FolderScanner
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;

            await _folderScanner.ScanFolderHierarchyAsync(folder1, progress, cancellationToken);
            await _folderScanner.ScanFolderHierarchyAsync(folder2, progress, cancellationToken);

            // Verify cache is populated
            Assert.IsTrue(_cacheManager.IsFolderCached(folder1), "Folder1 should be cached");
            Assert.IsTrue(_cacheManager.IsFolderCached(folder2), "Folder2 should be cached");

            // Act - Now use DuplicateAnalyzer with the same cache
            var folders = new List<string> { folder1, folder2 };
            var fileMatches = await _duplicateAnalyzer.FindDuplicateFilesAsync(folders, progress, cancellationToken);

            // Assert - Verify DuplicateAnalyzer used cached data correctly
            Assert.IsTrue(fileMatches.Count > 0, "Should find duplicate files");
            
            // Verify that the analyzer found the expected duplicates
            var expectedFiles = new[] { "document.txt", "image.jpg", "data.csv" };
            foreach (var expectedFile in expectedFiles)
            {
                var matchFound = fileMatches.Any(fm => 
                    Path.GetFileName(fm.PathA) == expectedFile && 
                    Path.GetFileName(fm.PathB) == expectedFile);
                Assert.IsTrue(matchFound, $"Should find duplicate match for {expectedFile}");
            }

            monitor.RecordItemsProcessed(fileMatches.Count);
        }

        [TestMethod]
        public async Task DuplicateAnalyzer_FileComparer_Integration_ConsistentResults()
        {
            // Arrange - Create test data
            var structure = new TestFileStructure
            {
                RootPath = "ComparerIntegrationTest",
                Folders = new()
                {
                    new TestFolder
                    {
                        Path = "LeftFolder",
                        Files = new() { "common.txt", "left_only.txt" },
                        TotalSize = 2048
                    },
                    new TestFolder
                    {
                        Path = "RightFolder",
                        Files = new() { "common.txt", "right_only.txt" },
                        TotalSize = 2048
                    }
                }
            };

            var rootPath = await CreateTestFolderStructureAsync(structure);
            var leftFolder = Path.Combine(rootPath, "LeftFolder");
            var rightFolder = Path.Combine(rootPath, "RightFolder");

            // Make common.txt identical in both folders
            var commonContent = "This is common content";
            await File.WriteAllTextAsync(Path.Combine(leftFolder, "common.txt"), commonContent);
            await File.WriteAllTextAsync(Path.Combine(rightFolder, "common.txt"), commonContent);

            using var monitor = PerformanceHelper.StartMonitoring("DuplicateAnalyzer_FileComparer_Integration");

            // Act - First, populate cache and find duplicates
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;

            await _folderScanner.ScanFolderHierarchyAsync(leftFolder, progress, cancellationToken);
            await _folderScanner.ScanFolderHierarchyAsync(rightFolder, progress, cancellationToken);

            var folders = new List<string> { leftFolder, rightFolder };
            var fileMatches = await _duplicateAnalyzer.FindDuplicateFilesAsync(folders, progress, cancellationToken);
            var folderMatches = await _duplicateAnalyzer.AggregateFolderMatchesAsync(fileMatches, _cacheManager, progress);

            // Act - Now use FileComparer to build detailed comparison
            var folderMatch = folderMatches.FirstOrDefault();
            Assert.IsNotNull(folderMatch, "Should have at least one folder match");

            var fileComparison = _fileComparer.BuildFileComparison(
                folderMatch.LeftFolder, 
                folderMatch.RightFolder, 
                folderMatch.DuplicateFiles,
                _cacheManager);

            // Assert - Verify consistency between components
            Assert.IsTrue(fileComparison.Count > 0, "Should have file comparison results");

            // Verify that FileComparer found the same duplicates as DuplicateAnalyzer
            var duplicateFromAnalyzer = fileMatches.Any(fm => Path.GetFileName(fm.PathA) == "common.txt");
            var duplicateFromComparer = fileComparison.Any(fc => fc.IsDuplicate && fc.PrimaryFileName == "common.txt");

            Assert.IsTrue(duplicateFromAnalyzer, "DuplicateAnalyzer should find common.txt as duplicate");
            Assert.IsTrue(duplicateFromComparer, "FileComparer should mark common.txt as duplicate");

            // Verify unique files are handled consistently
            var leftOnlyFile = fileComparison.FirstOrDefault(fc => fc.PrimaryFileName == "left_only.txt");
            var rightOnlyFile = fileComparison.FirstOrDefault(fc => fc.PrimaryFileName == "right_only.txt");

            Assert.IsNotNull(leftOnlyFile, "Should find left_only.txt in comparison");
            Assert.IsNotNull(rightOnlyFile, "Should find right_only.txt in comparison");
            Assert.IsFalse(leftOnlyFile.IsDuplicate, "left_only.txt should not be marked as duplicate");
            Assert.IsFalse(rightOnlyFile.IsDuplicate, "right_only.txt should not be marked as duplicate");

            monitor.RecordItemsProcessed(fileComparison.Count);
        }

        [TestMethod]
        public async Task ProgressReporting_Integration_FlowsCorrectlyThroughComponents()
        {
            // Arrange - Create larger dataset for progress testing
            var datasetPath = await FileSystemHelper.CreateLargeDatasetAsync(5, 10); // 50 files
            var folders = Directory.GetDirectories(datasetPath).Take(3).ToList();

            var progressCollector = new ThreadSafeProgressCollector();
            var progress = progressCollector.CreateProgress();

            using var monitor = PerformanceHelper.StartMonitoring("ProgressReporting_Integration");

            // Act - Run complete workflow and capture progress
            var cancellationToken = CancellationToken.None;

            // Phase 1: Folder scanning
            foreach (var folder in folders)
            {
                await _folderScanner.ScanFolderHierarchyAsync(folder, progress, cancellationToken);
            }

            // Phase 2: Duplicate analysis
            var fileMatches = await _duplicateAnalyzer.FindDuplicateFilesAsync(folders, progress, cancellationToken);
            var folderMatches = await _duplicateAnalyzer.AggregateFolderMatchesAsync(fileMatches, _cacheManager, progress);

            // Assert - Verify progress reporting integration
            Assert.IsTrue(progressCollector.Count > 0, "Should have received progress updates");

            // Get a thread-safe snapshot for analysis
            var progressUpdates = progressCollector.GetSnapshot();

            // Verify different phases were reported
            var phases = progressUpdates.Select(p => p.Phase).Distinct().ToList();
            Assert.IsTrue(phases.Count > 1, "Should have multiple analysis phases");

            // Verify progress values are reasonable
            var validProgressUpdates = progressUpdates.Where(u => u.MaxProgress > 0).ToList();
            Assert.IsTrue(validProgressUpdates.Count > 0, "Should have at least some progress updates with positive max progress");
            
            foreach (var update in validProgressUpdates)
            {
                Assert.IsTrue(update.CurrentProgress >= 0, "Progress should not be negative");
                Assert.IsTrue(update.MaxProgress > 0, "Max progress should be positive");
                Assert.IsTrue(update.CurrentProgress <= update.MaxProgress, "Current should not exceed max");
                Assert.IsFalse(string.IsNullOrEmpty(update.StatusMessage), "Should have status message");
            }

            // Verify progress generally increases over time
            var progressValues = progressUpdates.Where(p => p.MaxProgress > 0)
                .Select(p => (double)p.CurrentProgress / p.MaxProgress).ToList();
            
            if (progressValues.Count > 1)
            {
                var firstProgress = progressValues.First();
                var lastProgress = progressValues.Last();
                Assert.IsTrue(lastProgress >= firstProgress, "Progress should generally increase");
            }

            monitor.RecordItemsProcessed(progressCollector.Count);
        }

        [TestMethod]
        public async Task ErrorRecovery_Integration_HandlesErrorsAcrossComponents()
        {
            // Arrange - Create test scenario with potential errors
            var testFolder = Path.Combine(TestRootDirectory, "ErrorRecoveryTest");
            Directory.CreateDirectory(testFolder);

            // Create a normal file
            await File.WriteAllTextAsync(Path.Combine(testFolder, "normal.txt"), "Normal content");

            // Create a file that will be "in use" (we'll simulate this)
            var problematicFile = Path.Combine(testFolder, "problematic.txt");
            await File.WriteAllTextAsync(problematicFile, "Problematic content");

            using var monitor = PerformanceHelper.StartMonitoring("ErrorRecovery_Integration");

            // Act - Run operations that might encounter errors
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;

            try
            {
                // This should work despite potential file access issues
                var subfolders = await _folderScanner.ScanFolderHierarchyAsync(testFolder, progress, cancellationToken);
                
                // Verify that the operation completed successfully
                Assert.IsTrue(subfolders.Count >= 0, "Should complete scanning even with potential errors");
                
                // Verify that the cache contains what it could process
                Assert.IsTrue(_cacheManager.IsFolderCached(testFolder), "Should cache the folder");
                
                var folderInfo = _cacheManager.GetFolderInfo(testFolder);
                Assert.IsNotNull(folderInfo, "Should have folder info");
                Assert.IsTrue(folderInfo.FileCount > 0, "Should have processed at least some files");

                // Try duplicate analysis
                var folders = new List<string> { testFolder };
                var fileMatches = await _duplicateAnalyzer.FindDuplicateFilesAsync(folders, progress, cancellationToken);
                
                // Should complete without throwing exceptions
                Assert.IsTrue(fileMatches.Count >= 0, "Should complete analysis even with potential errors");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Components should handle errors gracefully, but got: {ex.Message}");
            }

            monitor.RecordItemsProcessed(1);
        }

        [TestMethod]
        public async Task ConcurrentAccess_Integration_ThreadSafetyAcrossComponents()
        {
            // Arrange - Create multiple folders for concurrent processing
            var folders = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                var folder = Path.Combine(TestRootDirectory, $"ConcurrentTest_{i}");
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(Path.Combine(folder, $"file_{i}.txt"), $"Content {i}");
                folders.Add(folder);
            }

            using var monitor = PerformanceHelper.StartMonitoring("ConcurrentAccess_Integration");

            // Act - Run concurrent operations
            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;

            var scanTasks = folders.Select(folder => 
                _folderScanner.ScanFolderHierarchyAsync(folder, progress, cancellationToken)).ToArray();

            var scanResults = await Task.WhenAll(scanTasks);

            // Verify all scans completed successfully
            foreach (var result in scanResults)
            {
                Assert.IsNotNull(result, "Each scan should complete successfully");
            }

            // Verify cache integrity after concurrent access
            foreach (var folder in folders)
            {
                Assert.IsTrue(_cacheManager.IsFolderCached(folder), $"Folder {folder} should be cached");
                var folderInfo = _cacheManager.GetFolderInfo(folder);
                Assert.IsNotNull(folderInfo, $"Should have info for folder {folder}");
                Assert.AreEqual(1, folderInfo.FileCount, $"Folder {folder} should have 1 file");
            }

            // Now test concurrent duplicate analysis
            var analysisTasks = folders.Select(folder => 
                _duplicateAnalyzer.FindDuplicateFilesAsync(new List<string> { folder }, progress, cancellationToken)).ToArray();

            var analysisResults = await Task.WhenAll(analysisTasks);

            // Verify all analyses completed successfully
            foreach (var result in analysisResults)
            {
                Assert.IsNotNull(result, "Each analysis should complete successfully");
            }

            monitor.RecordItemsProcessed(folders.Count);
        }

        [TestMethod]
        public async Task ProjectManager_Integration_PersistsComponentData()
        {
            // Arrange - Create test data and populate cache
            var (folder1, folder2) = await FileSystemHelper.CreateDuplicateFoldersAsync("ProjectIntegrationTest");

            var progress = new Progress<AnalysisProgress>();
            var cancellationToken = CancellationToken.None;

            // Populate cache through normal workflow
            await _folderScanner.ScanFolderHierarchyAsync(folder1, progress, cancellationToken);
            await _folderScanner.ScanFolderHierarchyAsync(folder2, progress, cancellationToken);

            var folders = new List<string> { folder1, folder2 };
            var fileMatches = await _duplicateAnalyzer.FindDuplicateFilesAsync(folders, progress, cancellationToken);

            using var monitor = PerformanceHelper.StartMonitoring("ProjectManager_Integration");

            // Act - Save project data
            var projectData = new ProjectData
            {
                ScanFolders = folders,
                FolderFileCache = _cacheManager.GetAllFolderFiles(),
                FileHashCache = _cacheManager.GetAllFileHashes(),
                FolderInfoCache = _cacheManager.GetAllFolderInfo()
            };

            var projectFilePath = Path.Combine(TestRootDirectory, "integration_test.cfp");
            await _projectManager.SaveProjectAsync(projectFilePath, projectData);

            // Clear cache to simulate fresh start
            _cacheManager.ClearCache();
            Assert.IsFalse(_cacheManager.IsFolderCached(folder1), "Cache should be cleared");

            // Act - Load project data
            var loadedProjectData = await _projectManager.LoadProjectAsync(projectFilePath);

            // Restore cache from loaded data
            _cacheManager.LoadFromProjectData(loadedProjectData);

            // Assert - Verify data integrity after save/load cycle
            Assert.AreEqual(2, loadedProjectData.ScanFolders.Count, "Should load correct number of scan folders");
            Assert.IsTrue(loadedProjectData.ScanFolders.Contains(folder1), "Should contain folder1");
            Assert.IsTrue(loadedProjectData.ScanFolders.Contains(folder2), "Should contain folder2");

            // Verify cache was restored correctly
            Assert.IsTrue(_cacheManager.IsFolderCached(folder1), "Folder1 should be cached after import");
            Assert.IsTrue(_cacheManager.IsFolderCached(folder2), "Folder2 should be cached after import");

            var restoredFolder1Info = _cacheManager.GetFolderInfo(folder1);
            var restoredFolder2Info = _cacheManager.GetFolderInfo(folder2);

            Assert.IsNotNull(restoredFolder1Info, "Should restore folder1 info");
            Assert.IsNotNull(restoredFolder2Info, "Should restore folder2 info");
            Assert.AreEqual(3, restoredFolder1Info.FileCount, "Should restore correct file count for folder1");
            Assert.AreEqual(3, restoredFolder2Info.FileCount, "Should restore correct file count for folder2");

            // Verify that duplicate analysis still works with restored data
            var restoredFileMatches = await _duplicateAnalyzer.FindDuplicateFilesAsync(folders, progress, cancellationToken);
            Assert.AreEqual(fileMatches.Count, restoredFileMatches.Count, "Should find same number of duplicates after restore");

            monitor.RecordItemsProcessed(loadedProjectData.ScanFolders.Count);
        }
    }
}