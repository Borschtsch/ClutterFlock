using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Unit.Core
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class FileComparerTests : TestBase
    {
        private FileComparer _fileComparer = null!;
        private MockCacheManager _mockCacheManager = null!;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _fileComparer = new FileComparer();
            _mockCacheManager = new MockCacheManager();
        }

        [TestMethod]
        public void BuildFileComparison_WithIdenticalFolders_ReturnsAllDuplicates()
        {
            // Arrange
            var leftFolder = @"C:\LeftFolder";
            var rightFolder = @"C:\RightFolder";
            
            var leftFiles = new List<string>
            {
                @"C:\LeftFolder\file1.txt",
                @"C:\LeftFolder\file2.jpg",
                @"C:\LeftFolder\file3.pdf"
            };
            
            var rightFiles = new List<string>
            {
                @"C:\RightFolder\file1.txt",
                @"C:\RightFolder\file2.jpg",
                @"C:\RightFolder\file3.pdf"
            };
            
            var duplicateFiles = new List<ClutterFlock.Models.FileMatch>
            {
                new(@"C:\LeftFolder\file1.txt", @"C:\RightFolder\file1.txt"),
                new(@"C:\LeftFolder\file2.jpg", @"C:\RightFolder\file2.jpg"),
                new(@"C:\LeftFolder\file3.pdf", @"C:\RightFolder\file3.pdf")
            };

            _mockCacheManager.SetupFolderFiles(leftFolder, leftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, rightFiles);

            // Create temporary files for testing
            var tempDir = CreateTempDirectory();
            var tempLeftDir = Path.Combine(tempDir, "Left");
            var tempRightDir = Path.Combine(tempDir, "Right");
            Directory.CreateDirectory(tempLeftDir);
            Directory.CreateDirectory(tempRightDir);

            foreach (var file in leftFiles)
            {
                var tempFile = Path.Combine(tempLeftDir, Path.GetFileName(file));
                File.WriteAllText(tempFile, "test content");
            }
            
            foreach (var file in rightFiles)
            {
                var tempFile = Path.Combine(tempRightDir, Path.GetFileName(file));
                File.WriteAllText(tempFile, "test content");
            }

            // Update mock to use temp files
            var tempLeftFiles = leftFiles.Select(f => Path.Combine(tempLeftDir, Path.GetFileName(f))).ToList();
            var tempRightFiles = rightFiles.Select(f => Path.Combine(tempRightDir, Path.GetFileName(f))).ToList();
            _mockCacheManager.SetupFolderFiles(leftFolder, tempLeftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, tempRightFiles);

            // Act
            var result = _fileComparer.BuildFileComparison(leftFolder, rightFolder, duplicateFiles, _mockCacheManager);

            // Assert
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result.All(f => f.IsDuplicate), "All files should be marked as duplicates");
            Assert.IsTrue(result.All(f => f.HasLeftFile && f.HasRightFile), "All files should exist in both folders");
        }

        [TestMethod]
        public void BuildFileComparison_WithPartialOverlap_ReturnsCorrectStatus()
        {
            // Arrange
            var leftFolder = @"C:\LeftFolder";
            var rightFolder = @"C:\RightFolder";
            
            var leftFiles = new List<string>
            {
                @"C:\LeftFolder\common.txt",
                @"C:\LeftFolder\leftonly.txt"
            };
            
            var rightFiles = new List<string>
            {
                @"C:\RightFolder\common.txt",
                @"C:\RightFolder\rightonly.txt"
            };
            
            var duplicateFiles = new List<ClutterFlock.Models.FileMatch>
            {
                new(@"C:\LeftFolder\common.txt", @"C:\RightFolder\common.txt")
            };

            _mockCacheManager.SetupFolderFiles(leftFolder, leftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, rightFiles);

            // Create temporary files
            var tempDir = CreateTempDirectory();
            var tempLeftDir = Path.Combine(tempDir, "Left");
            var tempRightDir = Path.Combine(tempDir, "Right");
            Directory.CreateDirectory(tempLeftDir);
            Directory.CreateDirectory(tempRightDir);

            File.WriteAllText(Path.Combine(tempLeftDir, "common.txt"), "common content");
            File.WriteAllText(Path.Combine(tempLeftDir, "leftonly.txt"), "left content");
            File.WriteAllText(Path.Combine(tempRightDir, "common.txt"), "common content");
            File.WriteAllText(Path.Combine(tempRightDir, "rightonly.txt"), "right content");

            var tempLeftFiles = new List<string>
            {
                Path.Combine(tempLeftDir, "common.txt"),
                Path.Combine(tempLeftDir, "leftonly.txt")
            };
            
            var tempRightFiles = new List<string>
            {
                Path.Combine(tempRightDir, "common.txt"),
                Path.Combine(tempRightDir, "rightonly.txt")
            };

            _mockCacheManager.SetupFolderFiles(leftFolder, tempLeftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, tempRightFiles);

            // Act
            var result = _fileComparer.BuildFileComparison(leftFolder, rightFolder, duplicateFiles, _mockCacheManager);

            // Assert
            Assert.AreEqual(3, result.Count);
            
            var commonFile = result.FirstOrDefault(f => f.PrimaryFileName == "common.txt");
            Assert.IsNotNull(commonFile);
            Assert.IsTrue(commonFile.IsDuplicate);
            Assert.IsTrue(commonFile.HasLeftFile && commonFile.HasRightFile);
            
            var leftOnlyFile = result.FirstOrDefault(f => f.PrimaryFileName == "leftonly.txt");
            Assert.IsNotNull(leftOnlyFile);
            Assert.IsFalse(leftOnlyFile.IsDuplicate);
            Assert.IsTrue(leftOnlyFile.HasLeftFile && !leftOnlyFile.HasRightFile);
            
            var rightOnlyFile = result.FirstOrDefault(f => f.PrimaryFileName == "rightonly.txt");
            Assert.IsNotNull(rightOnlyFile);
            Assert.IsFalse(rightOnlyFile.IsDuplicate);
            Assert.IsTrue(!rightOnlyFile.HasLeftFile && rightOnlyFile.HasRightFile);
        }

        [TestMethod]
        public void BuildFileComparison_WithEmptyFolders_ReturnsEmptyList()
        {
            // Arrange
            var leftFolder = @"C:\EmptyLeft";
            var rightFolder = @"C:\EmptyRight";
            var duplicateFiles = new List<ClutterFlock.Models.FileMatch>();

            _mockCacheManager.SetupFolderFiles(leftFolder, new List<string>());
            _mockCacheManager.SetupFolderFiles(rightFolder, new List<string>());

            // Act
            var result = _fileComparer.BuildFileComparison(leftFolder, rightFolder, duplicateFiles, _mockCacheManager);

            // Assert
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void BuildFileComparison_CaseInsensitiveFileNames_HandlesCorrectly()
        {
            // Arrange
            var leftFolder = @"C:\LeftFolder";
            var rightFolder = @"C:\RightFolder";
            
            var leftFiles = new List<string> { @"C:\LeftFolder\File1.TXT" };
            var rightFiles = new List<string> { @"C:\RightFolder\file1.txt" };
            var duplicateFiles = new List<ClutterFlock.Models.FileMatch>
            {
                new(@"C:\LeftFolder\File1.TXT", @"C:\RightFolder\file1.txt")
            };

            _mockCacheManager.SetupFolderFiles(leftFolder, leftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, rightFiles);

            // Create temporary files
            var tempDir = CreateTempDirectory();
            var tempLeftDir = Path.Combine(tempDir, "Left");
            var tempRightDir = Path.Combine(tempDir, "Right");
            Directory.CreateDirectory(tempLeftDir);
            Directory.CreateDirectory(tempRightDir);

            File.WriteAllText(Path.Combine(tempLeftDir, "File1.TXT"), "content");
            File.WriteAllText(Path.Combine(tempRightDir, "file1.txt"), "content");

            var tempLeftFiles = new List<string> { Path.Combine(tempLeftDir, "File1.TXT") };
            var tempRightFiles = new List<string> { Path.Combine(tempRightDir, "file1.txt") };

            _mockCacheManager.SetupFolderFiles(leftFolder, tempLeftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, tempRightFiles);

            // Act
            var result = _fileComparer.BuildFileComparison(leftFolder, rightFolder, duplicateFiles, _mockCacheManager);

            // Assert
            Assert.AreEqual(1, result.Count);
            var fileDetail = result[0];
            Assert.IsTrue(fileDetail.IsDuplicate);
            Assert.IsTrue(fileDetail.HasLeftFile && fileDetail.HasRightFile);
            Assert.AreEqual("File1.TXT", fileDetail.LeftFileName);
            Assert.AreEqual("file1.txt", fileDetail.RightFileName);
        }

        [TestMethod]
        public void BuildFileComparison_WithInaccessibleFiles_HandlesGracefully()
        {
            // Arrange
            var leftFolder = @"C:\LeftFolder";
            var rightFolder = @"C:\RightFolder";
            
            var leftFiles = new List<string> { @"C:\NonExistent\inaccessible.txt" };
            var rightFiles = new List<string> { @"C:\NonExistent\inaccessible.txt" };
            var duplicateFiles = new List<ClutterFlock.Models.FileMatch>();

            _mockCacheManager.SetupFolderFiles(leftFolder, leftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, rightFiles);

            // Act
            var result = _fileComparer.BuildFileComparison(leftFolder, rightFolder, duplicateFiles, _mockCacheManager);

            // Assert
            Assert.AreEqual(1, result.Count);
            var fileDetail = result[0];
            Assert.IsFalse(fileDetail.IsDuplicate);
            Assert.AreEqual("inaccessible.txt", fileDetail.LeftFileName);
            Assert.AreEqual("inaccessible.txt", fileDetail.RightFileName);
            Assert.AreEqual("N/A", fileDetail.LeftSizeDisplay);
            Assert.AreEqual("N/A", fileDetail.RightSizeDisplay);
            Assert.AreEqual("N/A", fileDetail.LeftDateDisplay);
            Assert.AreEqual("N/A", fileDetail.RightDateDisplay);
        }

        [TestMethod]
        public void BuildFileComparison_SortsFilesByName()
        {
            // Arrange
            var leftFolder = @"C:\LeftFolder";
            var rightFolder = @"C:\RightFolder";
            
            var leftFiles = new List<string>
            {
                @"C:\LeftFolder\zebra.txt",
                @"C:\LeftFolder\alpha.txt",
                @"C:\LeftFolder\beta.txt"
            };
            
            var rightFiles = new List<string>();
            var duplicateFiles = new List<ClutterFlock.Models.FileMatch>();

            _mockCacheManager.SetupFolderFiles(leftFolder, leftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, rightFiles);

            // Create temporary files
            var tempDir = CreateTempDirectory();
            var tempLeftDir = Path.Combine(tempDir, "Left");
            Directory.CreateDirectory(tempLeftDir);

            foreach (var file in leftFiles)
            {
                var tempFile = Path.Combine(tempLeftDir, Path.GetFileName(file));
                File.WriteAllText(tempFile, "content");
            }

            var tempLeftFiles = leftFiles.Select(f => Path.Combine(tempLeftDir, Path.GetFileName(f))).ToList();
            _mockCacheManager.SetupFolderFiles(leftFolder, tempLeftFiles);

            // Act
            var result = _fileComparer.BuildFileComparison(leftFolder, rightFolder, duplicateFiles, _mockCacheManager);

            // Assert
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("alpha.txt", result[0].PrimaryFileName);
            Assert.AreEqual("beta.txt", result[1].PrimaryFileName);
            Assert.AreEqual("zebra.txt", result[2].PrimaryFileName);
        }

        [TestMethod]
        public void FilterFileDetails_ShowUniqueFilesTrue_ReturnsAllFiles()
        {
            // Arrange
            var allFiles = new List<FileDetailInfo>
            {
                new() { IsDuplicate = true, LeftFileName = "duplicate.txt" },
                new() { IsDuplicate = false, LeftFileName = "unique.txt" }
            };

            // Act
            var result = _fileComparer.FilterFileDetails(allFiles, showUniqueFiles: true);

            // Assert
            Assert.AreEqual(2, result.Count);
            CollectionAssert.AreEqual(allFiles, result);
        }

        [TestMethod]
        public void FilterFileDetails_ShowUniqueFilesFalse_ReturnsOnlyDuplicates()
        {
            // Arrange
            var allFiles = new List<FileDetailInfo>
            {
                new() { IsDuplicate = true, LeftFileName = "duplicate.txt" },
                new() { IsDuplicate = false, LeftFileName = "unique.txt" },
                new() { IsDuplicate = true, LeftFileName = "another_duplicate.txt" }
            };

            // Act
            var result = _fileComparer.FilterFileDetails(allFiles, showUniqueFiles: false);

            // Assert
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.All(f => f.IsDuplicate));
            Assert.AreEqual("duplicate.txt", result[0].LeftFileName);
            Assert.AreEqual("another_duplicate.txt", result[1].LeftFileName);
        }

        [TestMethod]
        public void FilterFileDetails_EmptyList_ReturnsEmptyList()
        {
            // Arrange
            var allFiles = new List<FileDetailInfo>();

            // Act
            var resultShowAll = _fileComparer.FilterFileDetails(allFiles, showUniqueFiles: true);
            var resultShowDuplicates = _fileComparer.FilterFileDetails(allFiles, showUniqueFiles: false);

            // Assert
            Assert.AreEqual(0, resultShowAll.Count);
            Assert.AreEqual(0, resultShowDuplicates.Count);
        }

        [TestMethod]
        public void BuildFileComparison_PopulatesFileInfoCorrectly()
        {
            // Arrange
            var leftFolder = @"C:\LeftFolder";
            var rightFolder = @"C:\RightFolder";
            
            var tempDir = CreateTempDirectory();
            var tempLeftDir = Path.Combine(tempDir, "Left");
            var tempRightDir = Path.Combine(tempDir, "Right");
            Directory.CreateDirectory(tempLeftDir);
            Directory.CreateDirectory(tempRightDir);

            var leftFile = Path.Combine(tempLeftDir, "test.txt");
            var rightFile = Path.Combine(tempRightDir, "test.txt");
            
            File.WriteAllText(leftFile, "Left content with more text");
            File.WriteAllText(rightFile, "Right content");
            
            // Set different modification times
            File.SetLastWriteTime(leftFile, new DateTime(2023, 1, 1, 10, 30, 0));
            File.SetLastWriteTime(rightFile, new DateTime(2023, 6, 15, 14, 45, 30));

            var leftFiles = new List<string> { leftFile };
            var rightFiles = new List<string> { rightFile };
            var duplicateFiles = new List<FileMatch>
            {
                new(leftFile, rightFile)
            };

            _mockCacheManager.SetupFolderFiles(leftFolder, leftFiles);
            _mockCacheManager.SetupFolderFiles(rightFolder, rightFiles);

            // Act
            var result = _fileComparer.BuildFileComparison(leftFolder, rightFolder, duplicateFiles, _mockCacheManager);

            // Assert
            Assert.AreEqual(1, result.Count);
            var fileDetail = result[0];
            
            Assert.IsTrue(fileDetail.IsDuplicate);
            Assert.AreEqual("test.txt", fileDetail.LeftFileName);
            Assert.AreEqual("test.txt", fileDetail.RightFileName);
            
            // Check file sizes
            Assert.IsTrue(fileDetail.LeftSizeBytes > fileDetail.RightSizeBytes);
            Assert.IsTrue(fileDetail.LeftSizeDisplay.Contains("B"));
            Assert.IsTrue(fileDetail.RightSizeDisplay.Contains("B"));
            
            // Check dates
            Assert.AreEqual(new DateTime(2023, 1, 1, 10, 30, 0), fileDetail.LeftDate);
            Assert.AreEqual(new DateTime(2023, 6, 15, 14, 45, 30), fileDetail.RightDate);
            Assert.AreEqual("2023-01-01 10:30", fileDetail.LeftDateDisplay);
            Assert.AreEqual("2023-06-15 14:45", fileDetail.RightDateDisplay);
            
            // Check full paths
            Assert.AreEqual(leftFile, fileDetail.LeftFullPath);
            Assert.AreEqual(rightFile, fileDetail.RightFullPath);
        }

        [TestMethod]
        public void FileDetailInfo_TooltipText_FormatsCorrectly()
        {
            // Arrange & Act
            var bothFiles = new FileDetailInfo
            {
                LeftFileName = "test.txt",
                LeftFullPath = @"C:\Left\test.txt",
                LeftDateDisplay = "2023-01-01 10:30",
                RightFileName = "test.txt",
                RightFullPath = @"C:\Right\test.txt",
                RightDateDisplay = "2023-06-15 14:45"
            };

            var leftOnly = new FileDetailInfo
            {
                LeftFileName = "left.txt",
                LeftFullPath = @"C:\Left\left.txt",
                LeftDateDisplay = "2023-01-01 10:30"
            };

            var rightOnly = new FileDetailInfo
            {
                RightFileName = "right.txt",
                RightFullPath = @"C:\Right\right.txt",
                RightDateDisplay = "2023-06-15 14:45"
            };

            // Assert
            Assert.IsTrue(bothFiles.TooltipText.Contains("Left: C:\\Left\\test.txt"));
            Assert.IsTrue(bothFiles.TooltipText.Contains("Right: C:\\Right\\test.txt"));
            Assert.IsTrue(bothFiles.TooltipText.Contains("2023-01-01 10:30"));
            Assert.IsTrue(bothFiles.TooltipText.Contains("2023-06-15 14:45"));

            Assert.IsTrue(leftOnly.TooltipText.Contains("Left only: C:\\Left\\left.txt"));
            Assert.IsTrue(leftOnly.TooltipText.Contains("2023-01-01 10:30"));

            Assert.IsTrue(rightOnly.TooltipText.Contains("Right only: C:\\Right\\right.txt"));
            Assert.IsTrue(rightOnly.TooltipText.Contains("2023-06-15 14:45"));
        }

        [TestMethod]
        public void FileDetailInfo_PrimaryFileName_ReturnsCorrectName()
        {
            // Arrange & Act
            var leftOnly = new FileDetailInfo { LeftFileName = "left.txt" };
            var rightOnly = new FileDetailInfo { RightFileName = "right.txt" };
            var bothFiles = new FileDetailInfo { LeftFileName = "left.txt", RightFileName = "right.txt" };

            // Assert
            Assert.AreEqual("left.txt", leftOnly.PrimaryFileName);
            Assert.AreEqual("right.txt", rightOnly.PrimaryFileName);
            Assert.AreEqual("left.txt", bothFiles.PrimaryFileName); // Left takes precedence
        }


    }
}