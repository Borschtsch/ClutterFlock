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

namespace ClutterFlock.Tests.Integration
{
    /// <summary>
    /// Debug tests to understand why workflow tests are failing
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [TestCategory("Debug")]
    public class DebugWorkflowTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task Debug_ComponentByComponent_UnderstandFailure()
        {
            // Step 1: Create test folders with identical files
            var folder1 = Path.Combine(TestRootDirectory, "Debug1");
            var folder2 = Path.Combine(TestRootDirectory, "Debug2");
            
            Directory.CreateDirectory(folder1);
            Directory.CreateDirectory(folder2);
            
            // Create identical files
            var content = "This is identical content for debugging";
            await File.WriteAllTextAsync(Path.Combine(folder1, "test.txt"), content);
            await File.WriteAllTextAsync(Path.Combine(folder2, "test.txt"), content);
            
            Console.WriteLine($"Created folders: {folder1}, {folder2}");
            Console.WriteLine($"Files in folder1: {string.Join(", ", Directory.GetFiles(folder1))}");
            Console.WriteLine($"Files in folder2: {string.Join(", ", Directory.GetFiles(folder2))}");

            // Step 2: Test components individually
            var cacheManager = new CacheManager();
            var errorRecoveryService = new ErrorRecoveryService();
            var folderScanner = new FolderScanner(cacheManager, errorRecoveryService);
            var duplicateAnalyzer = new DuplicateAnalyzer(cacheManager, errorRecoveryService);

            var progress = new Progress<AnalysisProgress>(p => 
                Console.WriteLine($"Progress: {p.Phase} - {p.StatusMessage} ({p.CurrentProgress}/{p.MaxProgress})"));
            var cancellationToken = CancellationToken.None;

            // Step 3: Test folder scanning
            Console.WriteLine("\n=== Testing Folder Scanning ===");
            var subfolders1 = await folderScanner.ScanFolderHierarchyAsync(folder1, progress, cancellationToken);
            var subfolders2 = await folderScanner.ScanFolderHierarchyAsync(folder2, progress, cancellationToken);
            
            Console.WriteLine($"Subfolders1: {string.Join(", ", subfolders1)}");
            Console.WriteLine($"Subfolders2: {string.Join(", ", subfolders2)}");
            Console.WriteLine($"Folder1 cached: {cacheManager.IsFolderCached(folder1)}");
            Console.WriteLine($"Folder2 cached: {cacheManager.IsFolderCached(folder2)}");

            if (cacheManager.IsFolderCached(folder1))
            {
                var folder1Info = cacheManager.GetFolderInfo(folder1);
                Console.WriteLine($"Folder1 info: {folder1Info?.FileCount} files, {folder1Info?.TotalSize} bytes");
                Console.WriteLine($"Folder1 files: {string.Join(", ", folder1Info?.Files ?? new List<string>())}");
            }

            if (cacheManager.IsFolderCached(folder2))
            {
                var folder2Info = cacheManager.GetFolderInfo(folder2);
                Console.WriteLine($"Folder2 info: {folder2Info?.FileCount} files, {folder2Info?.TotalSize} bytes");
                Console.WriteLine($"Folder2 files: {string.Join(", ", folder2Info?.Files ?? new List<string>())}");
            }

            // Step 4: Test duplicate analysis
            Console.WriteLine("\n=== Testing Duplicate Analysis ===");
            var allFolders = new List<string> { folder1, folder2 };
            var cachedFolders = cacheManager.GetAllFolderFiles().Keys.ToList();
            Console.WriteLine($"All cached folders: {string.Join(", ", cachedFolders)}");

            var fileMatches = await duplicateAnalyzer.FindDuplicateFilesAsync(allFolders, progress, cancellationToken);
            Console.WriteLine($"File matches found: {fileMatches.Count}");
            
            foreach (var match in fileMatches)
            {
                Console.WriteLine($"Match: {match.PathA} <-> {match.PathB}");
            }

            // Step 5: Test folder match aggregation
            Console.WriteLine("\n=== Testing Folder Match Aggregation ===");
            var folderMatches = await duplicateAnalyzer.AggregateFolderMatchesAsync(fileMatches, cacheManager, progress);
            Console.WriteLine($"Folder matches found: {folderMatches.Count}");
            
            foreach (var match in folderMatches)
            {
                Console.WriteLine($"Folder match: {match.LeftFolder} <-> {match.RightFolder}");
                Console.WriteLine($"  Similarity: {match.SimilarityPercentage}%");
                Console.WriteLine($"  Duplicate files: {match.DuplicateFiles.Count}");
            }

            // Step 6: Test MainViewModel workflow
            Console.WriteLine("\n=== Testing MainViewModel Workflow ===");
            var mainViewModel = new MainViewModel();
            
            var addResult1 = await mainViewModel.AddFolderAsync(folder1);
            var addResult2 = await mainViewModel.AddFolderAsync(folder2);
            
            Console.WriteLine($"Add folder1 result: {addResult1}, Status: {mainViewModel.StatusMessage}");
            Console.WriteLine($"Add folder2 result: {addResult2}, Status: {mainViewModel.StatusMessage}");
            Console.WriteLine($"Scan folders count: {mainViewModel.ScanFolders.Count}");
            Console.WriteLine($"Can run comparison: {mainViewModel.CanRunComparison}");

            var analysisResult = await mainViewModel.RunComparisonAsync();
            Console.WriteLine($"Analysis result: {analysisResult}, Status: {mainViewModel.StatusMessage}");
            Console.WriteLine($"Filtered matches count: {mainViewModel.FilteredFolderMatches.Count}");

            // Assertions for debugging
            Assert.IsTrue(addResult1, "Should add folder1");
            Assert.IsTrue(addResult2, "Should add folder2");
            Assert.AreEqual(2, mainViewModel.ScanFolders.Count, "Should have 2 scan folders");
            
            // The key assertion that's failing
            if (!analysisResult)
            {
                Console.WriteLine($"ANALYSIS FAILED: {mainViewModel.StatusMessage}");
            }
            
            if (mainViewModel.FilteredFolderMatches.Count == 0)
            {
                Console.WriteLine("NO FOLDER MATCHES FOUND - This is the problem!");
            }

            // For now, let's not fail the test, just gather information
            Console.WriteLine($"\n=== SUMMARY ===");
            Console.WriteLine($"Analysis successful: {analysisResult}");
            Console.WriteLine($"Folder matches: {mainViewModel.FilteredFolderMatches.Count}");
            Console.WriteLine($"Status: {mainViewModel.StatusMessage}");

            // Use assertions to show debug info in test output - check each step
            if (fileMatches.Count == 0)
            {
                Assert.Fail($"No file matches found. Cached folders: {string.Join(", ", cachedFolders)}. All folders passed to analyzer: {string.Join(", ", allFolders)}");
            }
            
            if (folderMatches.Count == 0)
            {
                Assert.Fail($"No folder matches found. File matches: {fileMatches.Count}");
            }
            
            if (!analysisResult)
            {
                Assert.Fail($"Analysis failed. Status: {mainViewModel.StatusMessage}");
            }
            
            if (mainViewModel.FilteredFolderMatches.Count == 0)
            {
                // Check filter settings
                Console.WriteLine($"Filter settings - MinSimilarity: {mainViewModel.MinimumSimilarity}%, MinSize: {mainViewModel.MinimumSizeMB}MB");
                
                // Try adjusting filters
                mainViewModel.MinimumSimilarity = 0.0;
                mainViewModel.MinimumSizeMB = 0.0;
                mainViewModel.ApplyFilters();
                
                // Wait for filtering to complete
                await Task.Delay(1000);
                
                Console.WriteLine($"After adjusting filters - Filtered matches: {mainViewModel.FilteredFolderMatches.Count}");
                
                if (mainViewModel.FilteredFolderMatches.Count == 0)
                {
                    Assert.Fail($"Still no filtered matches after adjusting filters. Analysis result: {analysisResult}, Status: {mainViewModel.StatusMessage}");
                }
                else
                {
                    Console.WriteLine("SUCCESS: Filters were the issue!");
                }
            }
        }
    }
}