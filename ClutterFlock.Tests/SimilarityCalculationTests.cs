namespace ClutterFlock.Tests
{
    // Test models - simplified versions for testing similarity calculation
    public sealed record FileMatch(string PathA, string PathB);

    public sealed class FolderMatch
    {
        public string LeftFolder { get; }
        public string RightFolder { get; }
        public List<FileMatch> DuplicateFiles { get; }
        public double SimilarityPercentage { get; }
        public long FolderSizeBytes { get; }

        public FolderMatch(string leftFolder, string rightFolder, List<FileMatch> duplicateFiles, 
            int totalLeftFiles, int totalRightFiles, long folderSizeBytes = 0)
        {
            LeftFolder = leftFolder;
            RightFolder = rightFolder;
            DuplicateFiles = duplicateFiles;
            FolderSizeBytes = folderSizeBytes;
            
            // Calculate Jaccard similarity: |A ∩ B| / |A ∪ B|
            // Union size = total files in both folders minus duplicates (to avoid double counting)
            var unionSize = totalLeftFiles + totalRightFiles - duplicateFiles.Count;
            SimilarityPercentage = unionSize > 0 
                ? (duplicateFiles.Count / (double)unionSize * 100.0) 
                : 0.0;
        }
    }

    [TestClass]
    public sealed class SimilarityCalculationTests
    {
        [TestMethod]
        public void SimilarityCalculation_IdenticalFolders_Returns100Percent()
        {
            // Arrange
            var leftFolder = @"C:\TestFolder1";
            var rightFolder = @"C:\TestFolder2";
            var duplicateFiles = new List<FileMatch>
            {
                new("file1.txt", "file1.txt"),
                new("file2.txt", "file2.txt"),
                new("file3.txt", "file3.txt")
            };
            int totalLeftFiles = 3;
            int totalRightFiles = 3;

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);

            // Assert
            Assert.AreEqual(100.0, folderMatch.SimilarityPercentage, 0.01, 
                "Identical folders should have 100% similarity");
        }

        [TestMethod]
        public void SimilarityCalculation_NoCommonFiles_Returns0Percent()
        {
            // Arrange
            var leftFolder = @"C:\TestFolder1";
            var rightFolder = @"C:\TestFolder2";
            var duplicateFiles = new List<FileMatch>(); // No duplicates
            int totalLeftFiles = 3;
            int totalRightFiles = 3;

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);

            // Assert
            Assert.AreEqual(0.0, folderMatch.SimilarityPercentage, 0.01, 
                "Folders with no common files should have 0% similarity");
        }

        [TestMethod]
        public void SimilarityCalculation_PartialOverlap_CalculatesJaccardCorrectly()
        {
            // Arrange
            var leftFolder = @"C:\TestFolder1";
            var rightFolder = @"C:\TestFolder2";
            var duplicateFiles = new List<FileMatch>
            {
                new("common1.txt", "common1.txt"),
                new("common2.txt", "common2.txt")
            };
            int totalLeftFiles = 4; // 2 common + 2 unique
            int totalRightFiles = 3; // 2 common + 1 unique
            
            // Expected: Jaccard = |intersection| / |union|
            // |intersection| = 2 (common files)
            // |union| = 4 + 3 - 2 = 5 (total files minus duplicates to avoid double counting)
            // Similarity = 2/5 * 100 = 40%

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);

            // Assert
            Assert.AreEqual(40.0, folderMatch.SimilarityPercentage, 0.01, 
                "Jaccard similarity should be calculated as intersection/union * 100");
        }

        [TestMethod]
        public void SimilarityCalculation_OneEmptyFolder_Returns0Percent()
        {
            // Arrange
            var leftFolder = @"C:\TestFolder1";
            var rightFolder = @"C:\EmptyFolder";
            var duplicateFiles = new List<FileMatch>(); // No duplicates possible
            int totalLeftFiles = 5;
            int totalRightFiles = 0; // Empty folder

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);

            // Assert
            Assert.AreEqual(0.0, folderMatch.SimilarityPercentage, 0.01, 
                "Comparison with empty folder should return 0% similarity");
        }

        [TestMethod]
        public void SimilarityCalculation_BothEmptyFolders_Returns0Percent()
        {
            // Arrange
            var leftFolder = @"C:\EmptyFolder1";
            var rightFolder = @"C:\EmptyFolder2";
            var duplicateFiles = new List<FileMatch>(); // No files to compare
            int totalLeftFiles = 0;
            int totalRightFiles = 0;

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);

            // Assert
            Assert.AreEqual(0.0, folderMatch.SimilarityPercentage, 0.01, 
                "Two empty folders should return 0% similarity (division by zero case)");
        }

        [TestMethod]
        public void SimilarityCalculation_SingleFileMatch_CalculatesCorrectly()
        {
            // Arrange
            var leftFolder = @"C:\TestFolder1";
            var rightFolder = @"C:\TestFolder2";
            var duplicateFiles = new List<FileMatch>
            {
                new("onlycommon.txt", "onlycommon.txt")
            };
            int totalLeftFiles = 1;
            int totalRightFiles = 1;
            
            // Expected: 1 common file, union = 1 + 1 - 1 = 1, similarity = 1/1 * 100 = 100%

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);

            // Assert
            Assert.AreEqual(100.0, folderMatch.SimilarityPercentage, 0.01, 
                "Single matching file in both folders should be 100% similar");
        }

        [TestMethod]
        public void SimilarityCalculation_LargeDataset_PerformanceTest()
        {
            // Arrange
            var leftFolder = @"C:\LargeFolder1";
            var rightFolder = @"C:\LargeFolder2";
            var duplicateFiles = new List<FileMatch>();
            
            // Create 500 duplicate files
            for (int i = 0; i < 500; i++)
            {
                duplicateFiles.Add(new FileMatch($"file{i}.txt", $"file{i}.txt"));
            }
            
            int totalLeftFiles = 1000; // 500 common + 500 unique
            int totalRightFiles = 750;  // 500 common + 250 unique
            
            // Expected: 500 / (1000 + 750 - 500) * 100 = 500/1250 * 100 = 40%

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);
            stopwatch.Stop();

            // Assert
            Assert.AreEqual(40.0, folderMatch.SimilarityPercentage, 0.01, 
                "Large dataset similarity calculation should be accurate");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 100, 
                "Similarity calculation should be fast even with large datasets");
        }

        [TestMethod]
        public void SimilarityCalculation_EdgeCase_AllFilesAreDuplicates()
        {
            // Arrange - scenario where all files in both folders are duplicates
            var leftFolder = @"C:\TestFolder1";
            var rightFolder = @"C:\TestFolder2";
            var duplicateFiles = new List<FileMatch>
            {
                new("file1.txt", "file1.txt"),
                new("file2.txt", "file2.txt")
            };
            int totalLeftFiles = 2; // All files are duplicates
            int totalRightFiles = 2; // All files are duplicates
            
            // Expected: 2 / (2 + 2 - 2) * 100 = 2/2 * 100 = 100%

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);

            // Assert
            Assert.AreEqual(100.0, folderMatch.SimilarityPercentage, 0.01, 
                "When all files are duplicates, similarity should be 100%");
        }

        [TestMethod]
        public void SimilarityCalculation_AsymmetricFolders_CalculatesCorrectly()
        {
            // Arrange - test with very different folder sizes
            var leftFolder = @"C:\SmallFolder";
            var rightFolder = @"C:\LargeFolder";
            var duplicateFiles = new List<FileMatch>
            {
                new("shared.txt", "shared.txt")
            };
            int totalLeftFiles = 2;   // 1 shared + 1 unique
            int totalRightFiles = 10; // 1 shared + 9 unique
            
            // Expected: 1 / (2 + 10 - 1) * 100 = 1/11 * 100 ≈ 9.09%

            // Act
            var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, totalLeftFiles, totalRightFiles);

            // Assert
            Assert.AreEqual(9.09, folderMatch.SimilarityPercentage, 0.01, 
                "Asymmetric folders should calculate similarity correctly");
        }
    }
}