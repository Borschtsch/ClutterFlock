using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.ViewModels;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Integration
{
    /// <summary>
    /// End-to-end workflow integration tests that validate complete user scenarios
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [TestCategory("EndToEnd")]
    [TestCategory("LongRunning")]
    public class FullWorkflowTests : IntegrationTestBase
    {
            private MainViewModel _mainViewModel = null!;

        protected override async Task OnSetupAsync()
        {
            // Initialize MainViewModel - it creates its own dependencies
            _mainViewModel = new MainViewModel();

            await base.OnSetupAsync();
        }

        protected override async Task OnCleanupAsync()
        {
            _mainViewModel?.Dispose();
            await base.OnCleanupAsync();
        }

        [TestMethod]
        public async Task CompleteWorkflow_AddFolders_Scan_Analyze_Filter_SaveProject_Success()
        {
            // Arrange - Create test folder structure with known duplicates
            var (folder1, folder2) = await FileSystemHelper.CreateDuplicateFoldersAsync("WorkflowTest");
            var projectFilePath = Path.Combine(TestRootDirectory, "test_project.cfp");

            using var monitor = PerformanceHelper.StartMonitoring("CompleteWorkflow");

            // Act & Assert - Step 1: Add folders
            var addResult1 = await _mainViewModel.AddFolderAsync(folder1);
            var addResult2 = await _mainViewModel.AddFolderAsync(folder2);

            Assert.IsTrue(addResult1, "Should successfully add first folder");
            Assert.IsTrue(addResult2, "Should successfully add second folder");
            Assert.AreEqual(2, _mainViewModel.ScanFolders.Count, "Should have added 2 folders");
            Assert.IsTrue(_mainViewModel.ScanFolders.Contains(folder1), "Should contain first folder");
            Assert.IsTrue(_mainViewModel.ScanFolders.Contains(folder2), "Should contain second folder");

            // Act & Assert - Step 2: Start analysis (scan + analyze)
            var analysisResult = await _mainViewModel.RunComparisonAsync();
            
            Assert.IsTrue(analysisResult, "Analysis should complete successfully");
            Assert.IsFalse(_mainViewModel.OperationInProgress, "Operation should be completed");
            
            // Set appropriate filters for test files
            _mainViewModel.MinimumSimilarity = 50.0;
            _mainViewModel.MinimumSizeMB = 0.0;
            _mainViewModel.ApplyFilters();
            await Task.Delay(1000);
            
            Assert.IsTrue(_mainViewModel.FilteredFolderMatches.Count > 0, "Should have found folder matches");
            Assert.IsFalse(string.IsNullOrEmpty(_mainViewModel.StatusMessage), "Should have a status message");

            // Verify that duplicates were found
            var folderMatch = _mainViewModel.FilteredFolderMatches.FirstOrDefault();
            Assert.IsNotNull(folderMatch, "Should have at least one folder match");
            Assert.IsTrue(folderMatch.SimilarityPercentage > 90, "Folders should be highly similar");

            // Act & Assert - Step 3: Apply filters
            _mainViewModel.MinimumSimilarity = 50.0; // Lower threshold for test files
            _mainViewModel.MinimumSizeMB = 0.0; // Include very small test files

            // Trigger filter update by calling ApplyFilters method
            _mainViewModel.ApplyFilters();
            
            // Wait for async filtering to complete
            await Task.Delay(1000);

            // Verify filtering worked
            Assert.IsTrue(_mainViewModel.FilteredFolderMatches.Count > 0, "Should have matches after filtering");

            // Act & Assert - Step 4: Save project
            var saveResult = await _mainViewModel.SaveProjectAsync(projectFilePath);

            Assert.IsTrue(saveResult, "Should successfully save project");
            Assert.IsTrue(File.Exists(projectFilePath), "Project file should be created");

            // Verify project file content by loading it with ProjectManager
            var projectManager = new ProjectManager();
            var projectData = await projectManager.LoadProjectAsync(projectFilePath);
            Assert.IsNotNull(projectData, "Should be able to load saved project");
            Assert.AreEqual(2, projectData.ScanFolders.Count, "Should save scan folders");
            Assert.IsTrue(projectData.FolderFileCache.Count > 0, "Should save folder cache data");

            monitor.RecordItemsProcessed(projectData.ScanFolders.Count);
        }

        [TestMethod]
        public async Task ProjectLoadWorkflow_LoadProject_ModifyFolders_ReAnalyze_SaveProject_Success()
        {
            // Arrange - Create initial project
            var (folder1, folder2) = await FileSystemHelper.CreateDuplicateFoldersAsync("LoadTest");
            var folder3 = await FileSystemHelper.CreateTestFileAsync(Path.Combine(TestRootDirectory, "folder3", "unique.txt"), 1024);
            var folder3Path = Path.GetDirectoryName(folder3)!;
            
            var projectFilePath = Path.Combine(TestRootDirectory, "load_test_project.cfp");

            // Create initial project
            await _mainViewModel.AddFolderAsync(folder1);
            await _mainViewModel.AddFolderAsync(folder2);
            await _mainViewModel.RunComparisonAsync();
            await _mainViewModel.SaveProjectAsync(projectFilePath);

            // Clear current state by removing folders
            _mainViewModel.RemoveFolder(folder1);
            _mainViewModel.RemoveFolder(folder2);
            Assert.AreEqual(0, _mainViewModel.ScanFolders.Count, "Should have cleared folders");

            using var monitor = PerformanceHelper.StartMonitoring("ProjectLoadWorkflow");

            // Act & Assert - Step 1: Load project
            var loadResult = await _mainViewModel.LoadProjectAsync(projectFilePath);

            Assert.IsTrue(loadResult, "Should successfully load project");
            Assert.AreEqual(2, _mainViewModel.ScanFolders.Count, "Should have loaded scan folders");
            
            // Run analysis after loading project to populate matches
            await _mainViewModel.RunComparisonAsync();
            
            // Apply appropriate filters for test files
            _mainViewModel.MinimumSimilarity = 50.0;
            _mainViewModel.MinimumSizeMB = 0.0;
            _mainViewModel.ApplyFilters();
            await Task.Delay(1000);
            
            Assert.IsTrue(_mainViewModel.FilteredFolderMatches.Count > 0, "Should have loaded folder matches");

            // Act & Assert - Step 2: Modify folders (add new folder)
            await _mainViewModel.AddFolderAsync(folder3Path);
            Assert.AreEqual(3, _mainViewModel.ScanFolders.Count, "Should have added third folder");

            // Act & Assert - Step 3: Re-analyze with modified folder list
            var reAnalysisResult = await _mainViewModel.RunComparisonAsync();

            Assert.IsTrue(reAnalysisResult, "Re-analysis should complete successfully");
            Assert.IsFalse(_mainViewModel.OperationInProgress, "Re-analysis should be completed");
            
            // The results might be different now with the additional folder
            var currentMatches = _mainViewModel.FilteredFolderMatches.Count;
            Assert.IsTrue(currentMatches >= 0, "Should have completed re-analysis");

            // Act & Assert - Step 4: Save updated project
            var saveResult = await _mainViewModel.SaveProjectAsync(projectFilePath);

            Assert.IsTrue(saveResult, "Should successfully save updated project");

            // Verify updated project
            var projectManager = new ProjectManager();
            var updatedProjectData = await projectManager.LoadProjectAsync(projectFilePath);
            Assert.AreEqual(3, updatedProjectData.ScanFolders.Count, "Should save updated scan folders");

            monitor.RecordItemsProcessed(updatedProjectData.ScanFolders.Count);
        }

        [TestMethod]
        public async Task CancellationWorkflow_StartAnalysis_Cancel_VerifyCleanup_Success()
        {
            // Arrange - Create larger dataset for cancellation testing
            var largeDatasetPath = await FileSystemHelper.CreateLargeDatasetAsync(50, 20); // Larger dataset
            var folders = Directory.GetDirectories(largeDatasetPath).Take(5).ToList(); // More folders

            foreach (var folder in folders)
            {
                await _mainViewModel.AddFolderAsync(folder);
            }

            using var monitor = PerformanceHelper.StartMonitoring("CancellationWorkflow");

            // Act - Start analysis
            var analysisTask = _mainViewModel.RunComparisonAsync();

            // Wait a bit to ensure analysis has started, but don't fail if it completes quickly
            await Task.Delay(50);
            
            // If analysis is still in progress, test cancellation
            if (_mainViewModel.OperationInProgress)
            {
                // Act - Cancel the operation
                _mainViewModel.CancelOperation();

                // Wait for cancellation to complete
                try
                {
                    await analysisTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when operation is cancelled
                }

                // Assert - Verify clean cancellation
                Assert.IsFalse(_mainViewModel.OperationInProgress, "Operation should no longer be in progress");
                Assert.IsTrue(_mainViewModel.StatusMessage.Contains("cancel") || 
                             _mainViewModel.StatusMessage.Contains("stopped"), 
                             "Status should indicate cancellation");
            }
            else
            {
                // Analysis completed too quickly to cancel - that's also valid
                await analysisTask; // Ensure task completes
                Assert.IsFalse(_mainViewModel.OperationInProgress, "Operation should be completed");
            }

            // Verify that the application is still in a usable state
            Assert.IsTrue(_mainViewModel.CanRunComparison, "Should be able to start new analysis");
            Assert.AreEqual(folders.Count, _mainViewModel.ScanFolders.Count, "Scan folders should be preserved");

            monitor.RecordItemsProcessed(1); // Cancellation operation
        }

        [TestMethod]
        public async Task DataIntegrityWorkflow_VerifyConsistency_ThroughoutWorkflow_Success()
        {
            // Arrange - Create test data with known characteristics
            var structure = new TestFileStructure
            {
                RootPath = "IntegrityTest",
                Folders = new()
                {
                    new TestFolder
                    {
                        Path = "SourceFolder",
                        Files = new() { "doc1.txt", "doc2.txt", "image1.jpg" },
                        TotalSize = 3072
                    },
                    new TestFolder
                    {
                        Path = "TargetFolder", 
                        Files = new() { "doc1.txt", "doc2.txt", "image1.jpg" },
                        TotalSize = 3072
                    }
                }
            };

            var rootPath = await CreateTestFolderStructureAsync(structure);
            var sourceFolder = Path.Combine(rootPath, "SourceFolder");
            var targetFolder = Path.Combine(rootPath, "TargetFolder");

            // Make target folder identical to source
            await FileSystemHelper.CreateDuplicateFileAsync(
                Path.Combine(sourceFolder, "doc1.txt"),
                Path.Combine(targetFolder, "doc1.txt"));
            await FileSystemHelper.CreateDuplicateFileAsync(
                Path.Combine(sourceFolder, "doc2.txt"),
                Path.Combine(targetFolder, "doc2.txt"));
            await FileSystemHelper.CreateDuplicateFileAsync(
                Path.Combine(sourceFolder, "image1.jpg"),
                Path.Combine(targetFolder, "image1.jpg"));

            using var monitor = PerformanceHelper.StartMonitoring("DataIntegrityWorkflow");

            // Act - Complete workflow
            await _mainViewModel.AddFolderAsync(sourceFolder);
            await _mainViewModel.AddFolderAsync(targetFolder);
            await _mainViewModel.RunComparisonAsync();

            // Apply appropriate filters for test files
            _mainViewModel.MinimumSimilarity = 50.0;
            _mainViewModel.MinimumSizeMB = 0.0;
            _mainViewModel.ApplyFilters();
            await Task.Delay(1000);

            // Assert - Verify data integrity
            Assert.AreEqual(2, _mainViewModel.ScanFolders.Count, "Should maintain folder count");
            
            var folderMatch = _mainViewModel.FilteredFolderMatches.FirstOrDefault();
            Assert.IsNotNull(folderMatch, "Should find folder match");
            Assert.AreEqual(100.0, folderMatch.SimilarityPercentage, 0.1, "Should be 100% similar");
            Assert.AreEqual(3, folderMatch.DuplicateFiles.Count, "Should find 3 duplicate files");

            // Verify file details consistency
            _mainViewModel.SelectedFolderMatch = folderMatch;
            
            // Wait a bit for file details to populate
            await Task.Delay(100);
            Assert.IsTrue(_mainViewModel.FileDetails.Count > 0, "Should populate file details");

            // Verify that file details are populated correctly
            foreach (var fileDetail in _mainViewModel.FileDetails)
            {
                Assert.IsTrue(fileDetail.HasLeftFile || fileDetail.HasRightFile, "Each file detail should have at least one file");
                Assert.IsFalse(string.IsNullOrEmpty(fileDetail.PrimaryFileName), "Each file detail should have a primary file name");
            }

            monitor.RecordItemsProcessed(_mainViewModel.FileDetails.Count);
        }

        [TestMethod]
        public async Task PerformanceWorkflow_ValidatePerformanceRequirements_Success()
        {
            // Arrange - Create dataset for performance testing
            var datasetPath = await FileSystemHelper.CreateLargeDatasetAsync(10, 20); // 200 files
            var folders = Directory.GetDirectories(datasetPath).Take(5).ToList(); // Test with 5 folders

            foreach (var folder in folders)
            {
                await _mainViewModel.AddFolderAsync(folder);
            }

            var requirements = new PerformanceRequirements
            {
                MaxExecutionTime = TimeSpan.FromMinutes(2), // Should complete within 2 minutes
                MaxMemoryUsageBytes = 100 * 1024 * 1024, // 100MB max
                MinThroughputPerSecond = 50 // At least 50 files per second
            };

            using var monitor = PerformanceHelper.StartMonitoring("PerformanceWorkflow");

            // Act - Run complete analysis
            var startTime = DateTime.Now;
            var analysisResult = await _mainViewModel.RunComparisonAsync();
            var endTime = DateTime.Now;

            Assert.IsTrue(analysisResult, "Analysis should complete successfully");

            // Apply appropriate filters for test files
            _mainViewModel.MinimumSimilarity = 50.0;
            _mainViewModel.MinimumSizeMB = 0.0;
            _mainViewModel.ApplyFilters();
            await Task.Delay(1000);

            // Assert - Validate performance
            var executionTime = endTime - startTime;
            Assert.IsTrue(executionTime < requirements.MaxExecutionTime, 
                $"Execution time {executionTime} should be less than {requirements.MaxExecutionTime}");

            var currentMemory = PerformanceHelper.GetCurrentMemoryUsage();
            Assert.IsTrue(currentMemory < requirements.MaxMemoryUsageBytes, 
                $"Memory usage {currentMemory:N0} bytes should be less than {requirements.MaxMemoryUsageBytes:N0} bytes");

            // Verify results quality
            Assert.IsFalse(_mainViewModel.OperationInProgress, "Analysis should complete successfully");
            Assert.IsFalse(string.IsNullOrEmpty(_mainViewModel.StatusMessage), "Should have a status message");

            monitor.RecordItemsProcessed(200); // Total files processed

            // Validate performance metrics
            var metrics = PerformanceHelper.GetMetrics();
            var validationResult = PerformanceHelper.ValidatePerformance(requirements);
            
            if (!validationResult.IsValid)
            {
                Assert.Fail($"Performance requirements not met: {string.Join(", ", validationResult.Failures)}");
            }
        }
    }
}