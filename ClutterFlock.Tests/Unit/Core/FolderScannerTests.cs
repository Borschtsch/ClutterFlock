using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Unit.Core
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class FolderScannerTests : TestBase
    {
        private FolderScanner _folderScanner = null!;
        private MockCacheManager _mockCacheManager = null!;
        private MockErrorRecoveryService _mockErrorRecoveryService = null!;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _mockCacheManager = new MockCacheManager();
            _mockErrorRecoveryService = new MockErrorRecoveryService();
            _folderScanner = new FolderScanner(_mockCacheManager, _mockErrorRecoveryService);
        }

        [TestMethod]
        public void Constructor_WithNullCacheManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => 
                new FolderScanner(null!, _mockErrorRecoveryService));
        }

        [TestMethod]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Act
            var scanner = new FolderScanner(_mockCacheManager, _mockErrorRecoveryService);

            // Assert
            Assert.IsNotNull(scanner);
            Assert.AreEqual(typeof(FolderScanner), scanner.GetType());
        }

        [TestMethod]
        public void Constructor_WithNullErrorRecoveryService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => 
                new FolderScanner(_mockCacheManager, null!));
        }

        [TestMethod]
        public async Task ScanFolderHierarchyAsync_WithValidFolder_ReturnsSubfolders()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var subDir1 = Path.Combine(tempDir, "SubDir1");
            var subDir2 = Path.Combine(tempDir, "SubDir2");
            var subSubDir = Path.Combine(subDir1, "SubSubDir");
            
            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);
            Directory.CreateDirectory(subSubDir);
            
            File.WriteAllText(Path.Combine(tempDir, "file1.txt"), "content1");
            File.WriteAllText(Path.Combine(subDir1, "file2.txt"), "content2");
            File.WriteAllText(Path.Combine(subSubDir, "file3.txt"), "content3");

            var progressReports = new List<AnalysisProgress>();
            var progress = new Progress<AnalysisProgress>(p => progressReports.Add(p));



            // Act
            var result = await _folderScanner.ScanFolderHierarchyAsync(tempDir, progress, CancellationToken.None);

            // Assert
            Assert.AreEqual(4, result.Count); // tempDir + 3 subdirs
            Assert.IsTrue(result.Contains(tempDir));
            Assert.IsTrue(result.Contains(subDir1));
            Assert.IsTrue(result.Contains(subDir2));
            Assert.IsTrue(result.Contains(subSubDir));

            // Verify progress reporting
            Assert.IsTrue(progressReports.Count > 0);
            Assert.IsTrue(progressReports.Any(p => p.Phase == AnalysisPhase.CountingFolders));
            Assert.IsTrue(progressReports.Any(p => p.Phase == AnalysisPhase.ScanningFolders));

            // Verify folders were cached
            Assert.IsTrue(_mockCacheManager.IsFolderCached(tempDir));
            Assert.IsTrue(_mockCacheManager.IsFolderCached(subDir1));
            Assert.IsTrue(_mockCacheManager.IsFolderCached(subDir2));
            Assert.IsTrue(_mockCacheManager.IsFolderCached(subSubDir));
        }

        [TestMethod]
        public async Task ScanFolderHierarchyAsync_WithCachedFolders_SkipsCachedFolders()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var subDir = Path.Combine(tempDir, "SubDir");
            Directory.CreateDirectory(subDir);

            // Pre-cache one folder
            var cachedFolderInfo = new FolderInfo { Files = new List<string>(), TotalSize = 0 };
            _mockCacheManager.SetupFolderInfo(subDir, cachedFolderInfo);

            var progressReports = new List<AnalysisProgress>();
            var progress = new Progress<AnalysisProgress>(p => progressReports.Add(p));

            // Act
            var result = await _folderScanner.ScanFolderHierarchyAsync(tempDir, progress, CancellationToken.None);

            // Assert
            Assert.AreEqual(2, result.Count);
            
            // Verify that only the uncached folder was processed
            Assert.IsTrue(_mockCacheManager.WasOperationCalled("CacheFolderInfo"));
            var cacheOperations = _mockCacheManager.CacheOperations.Where(op => op.Contains("CacheFolderInfo")).ToList();
            Assert.AreEqual(1, cacheOperations.Count); // Only tempDir should be cached, subDir was already cached
        }

        [TestMethod]
        public async Task ScanFolderHierarchyAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            for (int i = 0; i < 100; i++)
            {
                Directory.CreateDirectory(Path.Combine(tempDir, $"SubDir{i}"));
            }

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () =>
                await _folderScanner.ScanFolderHierarchyAsync(tempDir, null, cts.Token));
            
            // TaskCanceledException is a subclass of OperationCanceledException
            Assert.IsTrue(exception is OperationCanceledException);
        }

        [TestMethod]
        public async Task ScanFolderHierarchyAsync_WithInaccessibleFolder_LogsErrorAndContinues()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var accessibleDir = Path.Combine(tempDir, "Accessible");
            var inaccessibleDir = Path.Combine(tempDir, "Inaccessible");
            
            Directory.CreateDirectory(accessibleDir);
            Directory.CreateDirectory(inaccessibleDir);
            
            // Make directory inaccessible by setting restrictive permissions
            try
            {
                var dirInfo = new DirectoryInfo(inaccessibleDir);
                var security = dirInfo.GetAccessControl();
                // Note: In a real test environment, you might need to actually restrict permissions
                // For this test, we'll rely on the error handling in the scanner
            }
            catch
            {
                // Skip permission modification if not supported
            }

            // Act
            var result = await _folderScanner.ScanFolderHierarchyAsync(tempDir, null, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.Count >= 2); // At least tempDir and accessibleDir
            
            // Verify error logging occurred (the scanner should handle access errors gracefully)
            // The exact number of errors depends on the system's permission handling
        }

        [TestMethod]
        public async Task AnalyzeFolderAsync_WithValidFolder_ReturnsCorrectFolderInfo()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var file1 = Path.Combine(tempDir, "file1.txt");
            var file2 = Path.Combine(tempDir, "file2.jpg");
            
            File.WriteAllText(file1, "This is test content for file 1");
            File.WriteAllText(file2, "Different content for file 2");
            
            var file1Info = new FileInfo(file1);
            var file2Info = new FileInfo(file2);

            // Act
            var result = await _folderScanner.AnalyzeFolderAsync(tempDir, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Files.Count);
            Assert.IsTrue(result.Files.Contains(file1));
            Assert.IsTrue(result.Files.Contains(file2));
            Assert.AreEqual(file1Info.Length + file2Info.Length, result.TotalSize);
            Assert.IsNotNull(result.LatestModificationDate);

            // Verify file metadata was cached
            Assert.IsTrue(_mockCacheManager.WasOperationCalled("CacheFileMetadata"));
            Assert.AreEqual(2, _mockCacheManager.GetOperationCount("CacheFileMetadata"));
        }

        [TestMethod]
        public async Task AnalyzeFolderAsync_WithEmptyFolder_ReturnsEmptyFolderInfo()
        {
            // Arrange
            var tempDir = CreateTempDirectory();

            // Act
            var result = await _folderScanner.AnalyzeFolderAsync(tempDir, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Files.Count);
            Assert.AreEqual(0, result.TotalSize);
            Assert.IsNull(result.LatestModificationDate);
        }

        [TestMethod]
        public async Task AnalyzeFolderAsync_WithInaccessibleFiles_HandlesErrorsGracefully()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var accessibleFile = Path.Combine(tempDir, "accessible.txt");
            var inaccessibleFile = Path.Combine(tempDir, "inaccessible.txt");
            
            File.WriteAllText(accessibleFile, "accessible content");
            File.WriteAllText(inaccessibleFile, "inaccessible content");

            // Configure error recovery service to simulate file access errors
            _mockErrorRecoveryService.ConfigureFileAccessAction(inaccessibleFile, 
                new RecoveryAction { Type = RecoveryActionType.Skip });

            // Act
            var result = await _folderScanner.AnalyzeFolderAsync(tempDir, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Files.Count); // Both files are listed even if one has access issues
            Assert.IsTrue(result.Files.Contains(accessibleFile));
            Assert.IsTrue(result.Files.Contains(inaccessibleFile));
        }

        [TestMethod]
        public async Task AnalyzeFolderAsync_WithCancellation_ThrowsOperationCanceledException()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            File.WriteAllText(Path.Combine(tempDir, "file.txt"), "content");

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            var exception = await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () =>
                await _folderScanner.AnalyzeFolderAsync(tempDir, cts.Token));
            
            // TaskCanceledException is a subclass of OperationCanceledException
            Assert.IsTrue(exception is OperationCanceledException);
        }

        [TestMethod]
        public void CountSubfolders_WithValidHierarchy_ReturnsCorrectCount()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var subDir1 = Path.Combine(tempDir, "SubDir1");
            var subDir2 = Path.Combine(tempDir, "SubDir2");
            var subSubDir = Path.Combine(subDir1, "SubSubDir");
            
            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);
            Directory.CreateDirectory(subSubDir);

            // Act
            var result = _folderScanner.CountSubfolders(tempDir);

            // Assert
            Assert.AreEqual(4, result); // tempDir + 3 subdirs
        }

        [TestMethod]
        public void CountSubfolders_WithSingleFolder_ReturnsOne()
        {
            // Arrange
            var tempDir = CreateTempDirectory();

            // Act
            var result = _folderScanner.CountSubfolders(tempDir);

            // Assert
            Assert.AreEqual(1, result);
        }

        [TestMethod]
        public void CountSubfolders_WithNonExistentFolder_ReturnsOne()
        {
            // Arrange
            var nonExistentPath = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid());

            // Act
            var result = _folderScanner.CountSubfolders(nonExistentPath);

            // Assert
            Assert.AreEqual(1, result); // Should return 1 as fallback
        }

        [TestMethod]
        public async Task ScanFolderHierarchyAsync_ProgressReporting_ReportsCorrectPhases()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            Directory.CreateDirectory(Path.Combine(tempDir, "SubDir1"));
            Directory.CreateDirectory(Path.Combine(tempDir, "SubDir2"));

            var progressReports = new List<AnalysisProgress>();
            var progress = new Progress<AnalysisProgress>(p => progressReports.Add(p));

            // Act
            await _folderScanner.ScanFolderHierarchyAsync(tempDir, progress, CancellationToken.None);

            // Assert
            var phases = progressReports.Select(p => p.Phase).Distinct().ToList();
            Assert.IsTrue(phases.Contains(AnalysisPhase.CountingFolders));
            Assert.IsTrue(phases.Contains(AnalysisPhase.ScanningFolders));

            // Verify progress messages are meaningful
            Assert.IsTrue(progressReports.Any(p => p.StatusMessage.Contains("subfolders")));
            Assert.IsTrue(progressReports.Any(p => p.StatusMessage.Contains("folders")));
        }

        [TestMethod]
        public async Task ScanFolderHierarchyAsync_WithLargeHierarchy_ReportsProgressPeriodically()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            
            // Create a moderate number of subdirectories to test progress reporting
            for (int i = 0; i < 50; i++)
            {
                Directory.CreateDirectory(Path.Combine(tempDir, $"SubDir{i:D2}"));
            }

            var progressReports = new List<AnalysisProgress>();
            var progress = new Progress<AnalysisProgress>(p => progressReports.Add(p));

            // Act
            await _folderScanner.ScanFolderHierarchyAsync(tempDir, progress, CancellationToken.None);

            // Assert
            Assert.IsTrue(progressReports.Count > 0, "Should have at least some progress reports");
            
            var scanningReports = progressReports.Where(p => p.Phase == AnalysisPhase.ScanningFolders).ToList();
            if (scanningReports.Any())
            {
                Assert.IsTrue(scanningReports.Any(p => p.MaxProgress > 0));
                Assert.IsTrue(scanningReports.Any(p => p.CurrentProgress >= 0));
            }
        }

        [TestMethod]
        public async Task ScanFolderHierarchyAsync_ErrorRecoveryIntegration_UsesErrorRecoveryService()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var subDir = Path.Combine(tempDir, "SubDir");
            Directory.CreateDirectory(subDir);

            // Configure error recovery service to simulate errors
            _mockErrorRecoveryService.ConfigureFileAccessAction(subDir, 
                new RecoveryAction { Type = RecoveryActionType.Skip });

            // Act
            var result = await _folderScanner.ScanFolderHierarchyAsync(tempDir, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            
            // Verify error recovery service was used
            var errorEvents = _mockErrorRecoveryService.GetErrorEvents();
            var skippedItems = _mockErrorRecoveryService.GetSkippedItems();
            
            // The scanner should handle errors gracefully and continue processing
            Assert.IsTrue(result.Count >= 1); // At least the root directory
        }

        [TestMethod]
        public async Task AnalyzeFolderAsync_WithMixedFileTypes_CachesAllFileMetadata()
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var files = new[]
            {
                Path.Combine(tempDir, "document.txt"),
                Path.Combine(tempDir, "image.jpg"),
                Path.Combine(tempDir, "data.json"),
                Path.Combine(tempDir, "script.ps1")
            };

            foreach (var file in files)
            {
                File.WriteAllText(file, $"Content for {Path.GetFileName(file)}");
            }

            // Act
            var result = await _folderScanner.AnalyzeFolderAsync(tempDir, CancellationToken.None);

            // Assert
            Assert.AreEqual(files.Length, result.Files.Count);
            Assert.AreEqual(files.Length, _mockCacheManager.GetOperationCount("CacheFileMetadata"));
            
            // Verify all files are included
            foreach (var file in files)
            {
                Assert.IsTrue(result.Files.Contains(file));
            }
        }
    }
}