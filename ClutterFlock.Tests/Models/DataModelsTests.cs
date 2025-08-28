using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Models
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class DataModelsTests : TestBase
    {
        [TestMethod]
        public void FilterCriteria_DefaultValues_AreSetCorrectly()
        {
            // Act
            var criteria = new FilterCriteria();

            // Assert
            Assert.AreEqual(50.0, criteria.MinimumSimilarityPercent);
            Assert.AreEqual(1024 * 1024, criteria.MinimumSizeBytes); // 1MB
            Assert.IsNull(criteria.MinimumDate);
            Assert.IsNull(criteria.MaximumDate);
        }

        [TestMethod]
        public void FilterCriteria_PropertySetters_WorkCorrectly()
        {
            // Arrange
            var criteria = new FilterCriteria();
            var testDate = new DateTime(2023, 1, 1);

            // Act
            criteria.MinimumSimilarityPercent = 75.0;
            criteria.MinimumSizeBytes = 2048;
            criteria.MinimumDate = testDate;
            criteria.MaximumDate = testDate.AddDays(30);

            // Assert
            Assert.AreEqual(75.0, criteria.MinimumSimilarityPercent);
            Assert.AreEqual(2048, criteria.MinimumSizeBytes);
            Assert.AreEqual(testDate, criteria.MinimumDate);
            Assert.AreEqual(testDate.AddDays(30), criteria.MaximumDate);
        }

        [TestMethod]
        public void FolderMatch_Constructor_InitializesPropertiesCorrectly()
        {
            // Arrange
            var leftFolder = @"C:\Left";
            var rightFolder = @"C:\Right";
            var duplicateFiles = new List<FileMatch>
            {
                new(@"C:\Left\file1.txt", @"C:\Right\file1.txt"),
                new(@"C:\Left\file2.txt", @"C:\Right\file2.txt")
            };
            int totalLeftFiles = 3;
            int totalRightFiles = 4;
            long folderSizeBytes = 1024;

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles, folderSizeBytes);

            // Assert
            Assert.AreEqual(leftFolder, folderMatch.LeftFolder);
            Assert.AreEqual(rightFolder, folderMatch.RightFolder);
            Assert.AreEqual(duplicateFiles, folderMatch.DuplicateFiles);
            Assert.AreEqual(folderSizeBytes, folderMatch.FolderSizeBytes);
            
            // Jaccard similarity: 2 / (3 + 4 - 2) = 2/5 = 40%
            Assert.AreEqual(40.0, folderMatch.SimilarityPercentage, 0.01);
        }

        [TestMethod]
        public void FolderMatch_SimilarityCalculation_HandlesEdgeCases()
        {
            // Test case 1: No duplicates
            var noDuplicates = new FolderMatch(@"C:\Left", @"C:\Right", new List<FileMatch>(), 3, 3, 1024);
            Assert.AreEqual(0.0, noDuplicates.SimilarityPercentage, 0.01);

            // Test case 2: All files are duplicates
            var allDuplicates = new FolderMatch(@"C:\Left", @"C:\Right", 
                new List<FileMatch> { new(@"C:\Left\file1.txt", @"C:\Right\file1.txt") }, 1, 1, 1024);
            Assert.AreEqual(100.0, allDuplicates.SimilarityPercentage, 0.01);

            // Test case 3: Empty folders
            var emptyFolders = new FolderMatch(@"C:\Left", @"C:\Right", new List<FileMatch>(), 0, 0, 0);
            Assert.AreEqual(0.0, emptyFolders.SimilarityPercentage, 0.01);
        }

        [TestMethod]
        public void FolderMatch_BasicProperties_AreAccessible()
        {
            // Arrange
            var leftFolder = @"C:\Parent\TestFolder";
            var rightFolder = @"C:\Other\AnotherFolder";
            var duplicateFiles = new List<FileMatch>();
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, 1, 1, 1024);

            // Act & Assert - Test the basic properties that we know exist
            Assert.AreEqual(leftFolder, folderMatch.LeftFolder);
            Assert.AreEqual(rightFolder, folderMatch.RightFolder);
            Assert.AreEqual(duplicateFiles, folderMatch.DuplicateFiles);
            Assert.AreEqual(1024, folderMatch.FolderSizeBytes);
        }





        [TestMethod]
        public void RecoveryAction_DefaultConstructor_InitializesCorrectly()
        {
            // Act
            var action = new RecoveryAction();

            // Assert
            Assert.AreEqual(RecoveryActionType.Skip, action.Type);
            Assert.AreEqual(string.Empty, action.Message);
            Assert.AreEqual(string.Empty, action.SuggestedSolution);
            Assert.IsFalse(action.ShouldRetry);
            Assert.AreEqual(TimeSpan.Zero, action.RetryDelay);
        }

        [TestMethod]
        public void RecoveryAction_PropertySetters_WorkCorrectly()
        {
            // Arrange
            var action = new RecoveryAction();
            var testDelay = TimeSpan.FromSeconds(5);

            // Act
            action.Type = RecoveryActionType.Retry;
            action.Message = "Test message";
            action.SuggestedSolution = "Test solution";
            action.ShouldRetry = true;
            action.RetryDelay = testDelay;

            // Assert
            Assert.AreEqual(RecoveryActionType.Retry, action.Type);
            Assert.AreEqual("Test message", action.Message);
            Assert.AreEqual("Test solution", action.SuggestedSolution);
            Assert.IsTrue(action.ShouldRetry);
            Assert.AreEqual(testDelay, action.RetryDelay);
        }

        [TestMethod]
        public void AnalysisProgress_DefaultConstructor_InitializesCorrectly()
        {
            // Act
            var progress = new AnalysisProgress();

            // Assert
            Assert.AreEqual(AnalysisPhase.Idle, progress.Phase);
            Assert.AreEqual(string.Empty, progress.StatusMessage);
            Assert.AreEqual(0, progress.CurrentProgress);
            Assert.AreEqual(0, progress.MaxProgress);
            Assert.IsFalse(progress.IsIndeterminate);
        }

        [TestMethod]
        public void AnalysisProgress_PropertySetters_WorkCorrectly()
        {
            // Arrange
            var progress = new AnalysisProgress();

            // Act
            progress.Phase = AnalysisPhase.ComparingFiles;
            progress.StatusMessage = "Comparing files...";
            progress.CurrentProgress = 50;
            progress.MaxProgress = 100;
            progress.IsIndeterminate = true;

            // Assert
            Assert.AreEqual(AnalysisPhase.ComparingFiles, progress.Phase);
            Assert.AreEqual("Comparing files...", progress.StatusMessage);
            Assert.AreEqual(50, progress.CurrentProgress);
            Assert.AreEqual(100, progress.MaxProgress);
            Assert.IsTrue(progress.IsIndeterminate);
        }

        [TestMethod]
        public void ErrorSummary_DefaultConstructor_InitializesCorrectly()
        {
            // Act
            var summary = new ErrorSummary();

            // Assert
            Assert.AreEqual(0, summary.PermissionErrors);
            Assert.AreEqual(0, summary.NetworkErrors);
            Assert.AreEqual(0, summary.ResourceErrors);
            Assert.AreEqual(0, summary.SkippedFiles);
            Assert.IsNotNull(summary.ErrorMessages);
            Assert.AreEqual(0, summary.ErrorMessages.Count);
            Assert.IsNotNull(summary.SkippedPaths);
            Assert.AreEqual(0, summary.SkippedPaths.Count);
            Assert.AreEqual(DateTime.MinValue, summary.LastErrorTime);
        }

        [TestMethod]
        public void ErrorSummary_PropertySetters_WorkCorrectly()
        {
            // Arrange
            var summary = new ErrorSummary();
            var testTime = new DateTime(2023, 6, 15, 10, 30, 0);
            var errorMessages = new List<string> { "Error 1", "Error 2" };
            var skippedPaths = new List<string> { @"C:\path1", @"C:\path2" };

            // Act
            summary.PermissionErrors = 5;
            summary.NetworkErrors = 3;
            summary.ResourceErrors = 2;
            summary.SkippedFiles = 10;
            summary.ErrorMessages = errorMessages;
            summary.SkippedPaths = skippedPaths;
            summary.LastErrorTime = testTime;

            // Assert
            Assert.AreEqual(5, summary.PermissionErrors);
            Assert.AreEqual(3, summary.NetworkErrors);
            Assert.AreEqual(2, summary.ResourceErrors);
            Assert.AreEqual(10, summary.SkippedFiles);
            Assert.AreEqual(errorMessages, summary.ErrorMessages);
            Assert.AreEqual(skippedPaths, summary.SkippedPaths);
            Assert.AreEqual(testTime, summary.LastErrorTime);
        }

        [TestMethod]
        public void ErrorSummary_TotalErrors_CalculatesCorrectly()
        {
            // Arrange
            var summary = new ErrorSummary
            {
                PermissionErrors = 5,
                NetworkErrors = 3,
                ResourceErrors = 2
            };

            // Act & Assert
            Assert.AreEqual(10, summary.TotalErrors);
        }

        [TestMethod]
        public void ErrorSummary_HasErrors_ReturnsTrueWhenErrorsExist()
        {
            // Test with SkippedFiles
            var summaryWithSkipped = new ErrorSummary { SkippedFiles = 1 };
            Assert.IsTrue(summaryWithSkipped.HasErrors);

            // Test with PermissionErrors
            var summaryWithPermission = new ErrorSummary { PermissionErrors = 1 };
            Assert.IsTrue(summaryWithPermission.HasErrors);

            // Test with NetworkErrors
            var summaryWithNetwork = new ErrorSummary { NetworkErrors = 1 };
            Assert.IsTrue(summaryWithNetwork.HasErrors);

            // Test with ResourceErrors
            var summaryWithResource = new ErrorSummary { ResourceErrors = 1 };
            Assert.IsTrue(summaryWithResource.HasErrors);
        }

        [TestMethod]
        public void ErrorSummary_HasErrors_ReturnsFalseWhenNoErrors()
        {
            // Arrange
            var summary = new ErrorSummary();

            // Act & Assert
            Assert.IsFalse(summary.HasErrors);
        }

        [TestMethod]
        public void FolderInfo_FileCount_CalculatesCorrectly()
        {
            // Arrange
            var folderInfo = new FolderInfo
            {
                Files = new List<string> { "file1.txt", "file2.txt", "file3.txt" },
                TotalSize = 3072,
                LatestModificationDate = DateTime.Now
            };

            // Act & Assert
            Assert.AreEqual(3, folderInfo.FileCount);
        }

        [TestMethod]
        public void FolderInfo_EmptyFiles_ReturnsZeroCount()
        {
            // Arrange
            var folderInfo = new FolderInfo
            {
                Files = new List<string>(),
                TotalSize = 0
            };

            // Act & Assert
            Assert.AreEqual(0, folderInfo.FileCount);
        }

        [TestMethod]
        public void FileMetadata_AllProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var testDate = new DateTime(2023, 6, 15, 10, 30, 0);
            var metadata = new FileMetadata();

            // Act
            metadata.FileName = "test.txt";
            metadata.Size = 1024;
            metadata.LastWriteTime = testDate;

            // Assert
            Assert.AreEqual("test.txt", metadata.FileName);
            Assert.AreEqual(1024, metadata.Size);
            Assert.AreEqual(testDate, metadata.LastWriteTime);
        }

        [TestMethod]
        public void FileMatch_Constructor_InitializesCorrectly()
        {
            // Arrange & Act
            var fileMatch = new FileMatch(@"C:\Left\file.txt", @"C:\Right\file.txt");

            // Assert
            Assert.AreEqual(@"C:\Left\file.txt", fileMatch.PathA);
            Assert.AreEqual(@"C:\Right\file.txt", fileMatch.PathB);
        }

        [TestMethod]
        public void FileDetailInfo_PrimaryFileName_ReturnsLeftWhenBothExist()
        {
            // Arrange
            var fileDetail = new FileDetailInfo
            {
                LeftFileName = "left.txt",
                RightFileName = "right.txt"
            };

            // Act & Assert
            Assert.AreEqual("left.txt", fileDetail.PrimaryFileName);
        }

        [TestMethod]
        public void FileDetailInfo_PrimaryFileName_ReturnsRightWhenLeftEmpty()
        {
            // Arrange
            var fileDetail = new FileDetailInfo
            {
                LeftFileName = string.Empty,
                RightFileName = "right.txt"
            };

            // Act & Assert
            Assert.AreEqual("right.txt", fileDetail.PrimaryFileName);
        }

        [TestMethod]
        public void FileDetailInfo_HasLeftFile_ReturnsCorrectValue()
        {
            // Test with left file
            var fileDetailWithLeft = new FileDetailInfo { LeftFileName = "test.txt" };
            Assert.IsTrue(fileDetailWithLeft.HasLeftFile);

            // Test without left file
            var fileDetailWithoutLeft = new FileDetailInfo { LeftFileName = string.Empty };
            Assert.IsFalse(fileDetailWithoutLeft.HasLeftFile);

            // Test with null left file
            var fileDetailWithNullLeft = new FileDetailInfo { LeftFileName = null! };
            Assert.IsFalse(fileDetailWithNullLeft.HasLeftFile);
        }

        [TestMethod]
        public void FileDetailInfo_HasRightFile_ReturnsCorrectValue()
        {
            // Test with right file
            var fileDetailWithRight = new FileDetailInfo { RightFileName = "test.txt" };
            Assert.IsTrue(fileDetailWithRight.HasRightFile);

            // Test without right file
            var fileDetailWithoutRight = new FileDetailInfo { RightFileName = string.Empty };
            Assert.IsFalse(fileDetailWithoutRight.HasRightFile);

            // Test with null right file
            var fileDetailWithNullRight = new FileDetailInfo { RightFileName = null! };
            Assert.IsFalse(fileDetailWithNullRight.HasRightFile);
        }

        [TestMethod]
        public void FileDetailInfo_TooltipText_FormatsCorrectly()
        {
            // Test with both files
            var fileDetailBoth = new FileDetailInfo
            {
                LeftFileName = "left.txt",
                LeftFullPath = @"C:\Left\left.txt",
                LeftDateDisplay = "2023-06-15",
                RightFileName = "right.txt",
                RightFullPath = @"C:\Right\right.txt",
                RightDateDisplay = "2023-06-16"
            };
            var expectedBoth = $"Left: C:\\Left\\left.txt (2023-06-15)\nRight: C:\\Right\\right.txt (2023-06-16)";
            Assert.AreEqual(expectedBoth, fileDetailBoth.TooltipText);

            // Test with left file only
            var fileDetailLeft = new FileDetailInfo
            {
                LeftFileName = "left.txt",
                LeftFullPath = @"C:\Left\left.txt",
                LeftDateDisplay = "2023-06-15",
                RightFileName = string.Empty
            };
            var expectedLeft = $"Left only: C:\\Left\\left.txt (2023-06-15)";
            Assert.AreEqual(expectedLeft, fileDetailLeft.TooltipText);

            // Test with right file only
            var fileDetailRight = new FileDetailInfo
            {
                LeftFileName = string.Empty,
                RightFileName = "right.txt",
                RightFullPath = @"C:\Right\right.txt",
                RightDateDisplay = "2023-06-16"
            };
            var expectedRight = $"Right only: C:\\Right\\right.txt (2023-06-16)";
            Assert.AreEqual(expectedRight, fileDetailRight.TooltipText);
        }

        [TestMethod]
        public void ProjectData_AllProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var projectData = new ProjectData();
            var scanFolders = new List<string> { @"C:\Folder1", @"C:\Folder2" };
            var folderInfoCache = new Dictionary<string, FolderInfo>();
            var fileHashCache = new Dictionary<string, string>();
            var folderFileCache = new Dictionary<string, List<string>>();
            var testDate = new DateTime(2023, 6, 15);

            // Act
            projectData.ScanFolders = scanFolders;
            projectData.FolderInfoCache = folderInfoCache;
            projectData.FileHashCache = fileHashCache;
            projectData.FolderFileCache = folderFileCache;
            projectData.CreatedDate = testDate;
            projectData.Version = "1.0";
            projectData.ApplicationName = "TestApp";

            // Assert
            Assert.AreEqual(scanFolders, projectData.ScanFolders);
            Assert.AreEqual(folderInfoCache, projectData.FolderInfoCache);
            Assert.AreEqual(fileHashCache, projectData.FileHashCache);
            Assert.AreEqual(folderFileCache, projectData.FolderFileCache);
            Assert.AreEqual(testDate, projectData.CreatedDate);
            Assert.AreEqual("1.0", projectData.Version);
            Assert.AreEqual("TestApp", projectData.ApplicationName);
        }

        [TestMethod]
        public void FolderMatch_LatestModificationDate_CanBeSet()
        {
            // Arrange
            var match = new FolderMatch(@"C:\Test", @"C:\Test2", new List<FileMatch>(), 1, 1);
            var testDate = new DateTime(2023, 6, 15);

            // Act
            match.LatestModificationDate = testDate;

            // Assert
            Assert.AreEqual(testDate, match.LatestModificationDate);
        }

        [TestMethod]
        public void FolderMatch_LatestModificationDate_DefaultsToNull()
        {
            // Arrange & Act
            var match = new FolderMatch(@"C:\Test", @"C:\Test2", new List<FileMatch>(), 1, 1);

            // Assert
            Assert.IsNull(match.LatestModificationDate);
        }

        [TestMethod]
        public void FileDetailInfo_DefaultConstructor_InitializesCorrectly()
        {
            // Act
            var fileDetail = new FileDetailInfo();

            // Assert
            Assert.AreEqual(string.Empty, fileDetail.LeftFileName);
            Assert.AreEqual(string.Empty, fileDetail.LeftSizeDisplay);
            Assert.AreEqual(0, fileDetail.LeftSizeBytes);
            Assert.AreEqual(string.Empty, fileDetail.LeftDateDisplay);
            Assert.IsNull(fileDetail.LeftDate);
            Assert.AreEqual(string.Empty, fileDetail.LeftFullPath);
            
            Assert.AreEqual(string.Empty, fileDetail.RightFileName);
            Assert.AreEqual(string.Empty, fileDetail.RightSizeDisplay);
            Assert.AreEqual(0, fileDetail.RightSizeBytes);
            Assert.AreEqual(string.Empty, fileDetail.RightDateDisplay);
            Assert.IsNull(fileDetail.RightDate);
            Assert.AreEqual(string.Empty, fileDetail.RightFullPath);
            
            Assert.IsFalse(fileDetail.IsDuplicate);
        }

        [TestMethod]
        public void FileDetailInfo_AllProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var fileDetail = new FileDetailInfo();
            var testDate = new DateTime(2023, 6, 15, 10, 30, 0);

            // Act
            fileDetail.LeftFileName = "left.txt";
            fileDetail.LeftSizeDisplay = "1.5 KB";
            fileDetail.LeftSizeBytes = 1536;
            fileDetail.LeftDateDisplay = "2023-06-15 10:30";
            fileDetail.LeftDate = testDate;
            fileDetail.LeftFullPath = @"C:\Left\left.txt";
            
            fileDetail.RightFileName = "right.txt";
            fileDetail.RightSizeDisplay = "2.0 KB";
            fileDetail.RightSizeBytes = 2048;
            fileDetail.RightDateDisplay = "2023-06-15 11:00";
            fileDetail.RightDate = testDate.AddMinutes(30);
            fileDetail.RightFullPath = @"C:\Right\right.txt";
            
            fileDetail.IsDuplicate = true;

            // Assert
            Assert.AreEqual("left.txt", fileDetail.LeftFileName);
            Assert.AreEqual("1.5 KB", fileDetail.LeftSizeDisplay);
            Assert.AreEqual(1536, fileDetail.LeftSizeBytes);
            Assert.AreEqual("2023-06-15 10:30", fileDetail.LeftDateDisplay);
            Assert.AreEqual(testDate, fileDetail.LeftDate);
            Assert.AreEqual(@"C:\Left\left.txt", fileDetail.LeftFullPath);
            
            Assert.AreEqual("right.txt", fileDetail.RightFileName);
            Assert.AreEqual("2.0 KB", fileDetail.RightSizeDisplay);
            Assert.AreEqual(2048, fileDetail.RightSizeBytes);
            Assert.AreEqual("2023-06-15 11:00", fileDetail.RightDateDisplay);
            Assert.AreEqual(testDate.AddMinutes(30), fileDetail.RightDate);
            Assert.AreEqual(@"C:\Right\right.txt", fileDetail.RightFullPath);
            
            Assert.IsTrue(fileDetail.IsDuplicate);
        }

        [TestMethod]
        public void FileMetadata_DefaultConstructor_InitializesCorrectly()
        {
            // Act
            var metadata = new FileMetadata();

            // Assert
            Assert.AreEqual(string.Empty, metadata.FileName);
            Assert.AreEqual(0, metadata.Size);
            Assert.AreEqual(DateTime.MinValue, metadata.LastWriteTime);
        }

        [TestMethod]
        public void FolderInfo_DefaultConstructor_InitializesCorrectly()
        {
            // Act
            var folderInfo = new FolderInfo();

            // Assert
            Assert.IsNotNull(folderInfo.Files);
            Assert.AreEqual(0, folderInfo.Files.Count);
            Assert.AreEqual(0, folderInfo.TotalSize);
            Assert.AreEqual(0, folderInfo.FileCount);
            Assert.IsNull(folderInfo.LatestModificationDate);
        }

        [TestMethod]
        public void FolderInfo_AllProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var folderInfo = new FolderInfo();
            var files = new List<string> { "file1.txt", "file2.txt", "file3.txt" };
            var testDate = new DateTime(2023, 6, 15, 10, 30, 0);

            // Act
            folderInfo.Files = files;
            folderInfo.TotalSize = 3072;
            folderInfo.LatestModificationDate = testDate;

            // Assert
            Assert.AreEqual(files, folderInfo.Files);
            Assert.AreEqual(3072, folderInfo.TotalSize);
            Assert.AreEqual(3, folderInfo.FileCount);
            Assert.AreEqual(testDate, folderInfo.LatestModificationDate);
        }

        [TestMethod]
        public void ProjectData_DefaultConstructor_InitializesCorrectly()
        {
            // Act
            var projectData = new ProjectData();

            // Assert
            Assert.IsNotNull(projectData.ScanFolders);
            Assert.AreEqual(0, projectData.ScanFolders.Count);
            Assert.IsNotNull(projectData.FolderFileCache);
            Assert.AreEqual(0, projectData.FolderFileCache.Count);
            Assert.IsNotNull(projectData.FileHashCache);
            Assert.AreEqual(0, projectData.FileHashCache.Count);
            Assert.IsNotNull(projectData.FolderInfoCache);
            Assert.AreEqual(0, projectData.FolderInfoCache.Count);
            Assert.AreEqual("1.0", projectData.Version);
            Assert.AreEqual("ClutterFlock", projectData.ApplicationName);
            Assert.IsTrue(projectData.CreatedDate > DateTime.MinValue);
        }

        [TestMethod]
        public void AnalysisPhase_AllValues_AreAccessible()
        {
            // Test that all enum values are accessible
            var phases = Enum.GetValues<AnalysisPhase>();
            
            Assert.IsTrue(phases.Contains(AnalysisPhase.Idle));
            Assert.IsTrue(phases.Contains(AnalysisPhase.CountingFolders));
            Assert.IsTrue(phases.Contains(AnalysisPhase.ScanningFolders));
            Assert.IsTrue(phases.Contains(AnalysisPhase.BuildingFileIndex));
            Assert.IsTrue(phases.Contains(AnalysisPhase.ComparingFiles));
            Assert.IsTrue(phases.Contains(AnalysisPhase.AggregatingResults));
            Assert.IsTrue(phases.Contains(AnalysisPhase.PopulatingResults));
            Assert.IsTrue(phases.Contains(AnalysisPhase.Complete));
            Assert.IsTrue(phases.Contains(AnalysisPhase.Cancelled));
            Assert.IsTrue(phases.Contains(AnalysisPhase.Error));
        }

        [TestMethod]
        public void RecoveryActionType_AllValues_AreAccessible()
        {
            // Test that all enum values are accessible
            var types = Enum.GetValues<RecoveryActionType>();
            
            Assert.IsTrue(types.Contains(RecoveryActionType.Skip));
            Assert.IsTrue(types.Contains(RecoveryActionType.Retry));
            Assert.IsTrue(types.Contains(RecoveryActionType.RetryWithElevation));
            Assert.IsTrue(types.Contains(RecoveryActionType.ReduceParallelism));
            Assert.IsTrue(types.Contains(RecoveryActionType.PauseAndWait));
            Assert.IsTrue(types.Contains(RecoveryActionType.Abort));
        }

        [TestMethod]
        public void ResourceConstraintType_AllValues_AreAccessible()
        {
            // Test that all enum values are accessible
            var types = Enum.GetValues<ResourceConstraintType>();
            
            Assert.IsTrue(types.Contains(ResourceConstraintType.Memory));
            Assert.IsTrue(types.Contains(ResourceConstraintType.DiskSpace));
            Assert.IsTrue(types.Contains(ResourceConstraintType.FileHandles));
            Assert.IsTrue(types.Contains(ResourceConstraintType.NetworkBandwidth));
            Assert.IsTrue(types.Contains(ResourceConstraintType.CpuUsage));
        }

        [TestMethod]
        public void FilterCriteria_FileExtensions_CanBeSetAndRetrieved()
        {
            // Arrange
            var criteria = new FilterCriteria();
            var extensions = new List<string> { ".txt", ".jpg", ".pdf" };

            // Act
            criteria.FileExtensions = extensions;

            // Assert
            Assert.AreEqual(extensions, criteria.FileExtensions);
            Assert.AreEqual(3, criteria.FileExtensions.Count);
            Assert.IsTrue(criteria.FileExtensions.Contains(".txt"));
            Assert.IsTrue(criteria.FileExtensions.Contains(".jpg"));
            Assert.IsTrue(criteria.FileExtensions.Contains(".pdf"));
        }

        [TestMethod]
        public void FilterCriteria_DateRanges_CanBeSetAndRetrieved()
        {
            // Arrange
            var criteria = new FilterCriteria();
            var minDate = new DateTime(2023, 1, 1);
            var maxDate = new DateTime(2023, 12, 31);

            // Act
            criteria.MinimumDate = minDate;
            criteria.MaximumDate = maxDate;

            // Assert
            Assert.AreEqual(minDate, criteria.MinimumDate);
            Assert.AreEqual(maxDate, criteria.MaximumDate);
        }

        [TestMethod]
        public void RecoveryAction_AllProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var action = new RecoveryAction();
            var delay = TimeSpan.FromMinutes(5);

            // Act
            action.Type = RecoveryActionType.PauseAndWait;
            action.Message = "Custom message";
            action.SuggestedSolution = "Custom solution";
            action.ShouldRetry = true;
            action.RetryDelay = delay;

            // Assert
            Assert.AreEqual(RecoveryActionType.PauseAndWait, action.Type);
            Assert.AreEqual("Custom message", action.Message);
            Assert.AreEqual("Custom solution", action.SuggestedSolution);
            Assert.IsTrue(action.ShouldRetry);
            Assert.AreEqual(delay, action.RetryDelay);
        }

        [TestMethod]
        public void ErrorSummary_AllProperties_CanBeSetAndRetrieved()
        {
            // Arrange
            var summary = new ErrorSummary();
            var testTime = new DateTime(2023, 6, 15, 10, 30, 0);
            var errorMessages = new List<string> { "Error 1", "Error 2", "Error 3" };
            var skippedPaths = new List<string> { @"C:\path1", @"C:\path2", @"C:\path3" };

            // Act
            summary.SkippedFiles = 15;
            summary.PermissionErrors = 8;
            summary.NetworkErrors = 4;
            summary.ResourceErrors = 3;
            summary.SkippedPaths = skippedPaths;
            summary.ErrorMessages = errorMessages;
            summary.LastErrorTime = testTime;

            // Assert
            Assert.AreEqual(15, summary.SkippedFiles);
            Assert.AreEqual(8, summary.PermissionErrors);
            Assert.AreEqual(4, summary.NetworkErrors);
            Assert.AreEqual(3, summary.ResourceErrors);
            Assert.AreEqual(skippedPaths, summary.SkippedPaths);
            Assert.AreEqual(errorMessages, summary.ErrorMessages);
            Assert.AreEqual(testTime, summary.LastErrorTime);
            Assert.AreEqual(15, summary.TotalErrors); // 8 + 4 + 3
            Assert.IsTrue(summary.HasErrors);
        }

        [TestMethod]
        public void FileMatch_Equality_WorksCorrectly()
        {
            // Arrange
            var match1 = new FileMatch(@"C:\file1.txt", @"C:\file2.txt");
            var match2 = new FileMatch(@"C:\file1.txt", @"C:\file2.txt");
            var match3 = new FileMatch(@"C:\file1.txt", @"C:\file3.txt");

            // Assert
            Assert.AreEqual(match1, match2);
            Assert.AreNotEqual(match1, match3);
            Assert.AreEqual(match1.GetHashCode(), match2.GetHashCode());
        }

        [TestMethod]
        public void FolderMatch_ComplexSimilarityCalculation_HandlesEdgeCases()
        {
            // Test with large numbers
            var largeMatch = new FolderMatch(@"C:\Large1", @"C:\Large2", 
                new List<FileMatch> { new(@"C:\Large1\file.txt", @"C:\Large2\file.txt") }, 
                10000, 15000, 1073741824);
            
            // Jaccard: 1 / (10000 + 15000 - 1) = 1/24999 ≈ 0.004%
            Assert.AreEqual(0.004, largeMatch.SimilarityPercentage, 0.001);

            // Test with single file folders
            var singleMatch = new FolderMatch(@"C:\Single1", @"C:\Single2",
                new List<FileMatch> { new(@"C:\Single1\only.txt", @"C:\Single2\only.txt") },
                1, 1, 1024);
            
            // Jaccard: 1 / (1 + 1 - 1) = 1/1 = 100%
            Assert.AreEqual(100.0, singleMatch.SimilarityPercentage, 0.01);
        }
    }
}