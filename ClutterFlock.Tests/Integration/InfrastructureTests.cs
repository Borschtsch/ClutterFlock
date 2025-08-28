using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Threading.Tasks;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Integration
{
    /// <summary>
    /// Tests to verify the integration test infrastructure is working correctly
    /// </summary>
    [TestClass]
    [TestCategory("Integration")]
    [TestCategory("Infrastructure")]
    public class InfrastructureTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task TestFileSystemHelper_CreateTestStructure_CreatesExpectedFiles()
        {
            // Arrange
            var structure = new TestFileStructure
            {
                RootPath = "TestStructure",
                Folders = new()
                {
                    new TestFolder
                    {
                        Path = "Folder1",
                        Files = new() { "file1.txt", "file2.txt" },
                        TotalSize = 2048
                    },
                    new TestFolder
                    {
                        Path = "Folder2",
                        Files = new() { "file3.txt" },
                        TotalSize = 1024
                    }
                },
                Files = new()
                {
                    new TestFile
                    {
                        Path = "root_file.txt",
                        Size = 512,
                        LastWriteTime = DateTime.Now
                    }
                }
            };

            // Act
            var rootPath = await CreateTestFolderStructureAsync(structure);

            // Assert
            AssertDirectoryExists(rootPath);
            AssertDirectoryExists(Path.Combine(rootPath, "Folder1"));
            AssertDirectoryExists(Path.Combine(rootPath, "Folder2"));
            
            AssertFileExists(Path.Combine(rootPath, "Folder1", "file1.txt"));
            AssertFileExists(Path.Combine(rootPath, "Folder1", "file2.txt"));
            AssertFileExists(Path.Combine(rootPath, "Folder2", "file3.txt"));
            AssertFileExists(Path.Combine(rootPath, "root_file.txt"), 512);
        }

        [TestMethod]
        public async Task TestFileSystemHelper_CreateDuplicateFolders_CreatesIdenticalContent()
        {
            // Act
            var (folder1, folder2) = await FileSystemHelper.CreateDuplicateFoldersAsync("DuplicateTest");

            // Assert
            AssertDirectoryExists(folder1, 3);
            AssertDirectoryExists(folder2, 3);

            // Verify files exist in both folders
            var expectedFiles = new[] { "document.txt", "image.jpg", "data.csv" };
            foreach (var fileName in expectedFiles)
            {
                var file1 = Path.Combine(folder1, fileName);
                var file2 = Path.Combine(folder2, fileName);
                
                AssertFileExists(file1);
                AssertFileExists(file2);

                // Verify files have identical content
                var hash1 = await FileSystemHelper.ComputeFileHashAsync(file1);
                var hash2 = await FileSystemHelper.ComputeFileHashAsync(file2);
                Assert.AreEqual(hash1, hash2, $"Files should have identical hashes: {fileName}");
            }
        }

        [TestMethod]
        public async Task TestFileSystemHelper_CreateLargeDataset_CreatesExpectedStructure()
        {
            // Arrange
            const int folderCount = 5;
            const int filesPerFolder = 3;

            // Act
            var datasetPath = await FileSystemHelper.CreateLargeDatasetAsync(folderCount, filesPerFolder);

            // Assert
            AssertDirectoryExists(datasetPath, folderCount * filesPerFolder);

            // Verify folder structure
            for (int i = 0; i < folderCount; i++)
            {
                var folderPath = Path.Combine(datasetPath, $"Folder_{i:D3}");
                AssertDirectoryExists(folderPath, filesPerFolder);

                // Verify files in folder
                for (int j = 0; j < filesPerFolder; j++)
                {
                    var filePath = Path.Combine(folderPath, $"File_{j:D3}.txt");
                    AssertFileExists(filePath);
                }
            }
        }

        [TestMethod]
        public async Task PerformanceHelper_MonitorsResourceUsage()
        {
            // Arrange
            PerformanceHelper.StartMonitoring();

            // Act - Perform some work
            await FileSystemHelper.CreateLargeDatasetAsync(2, 5);
            await Task.Delay(100); // Simulate some processing time

            // Assert
            var metrics = PerformanceHelper.GetCurrentMetrics();
            Assert.IsTrue(metrics.ExecutionTime.TotalMilliseconds > 0, "Should have recorded execution time");
            Assert.IsTrue(metrics.MemoryUsageBytes > 0, "Should have recorded memory usage");
        }

        [TestMethod]
        public void IntegrationTestBase_ProvidesIsolatedTestEnvironment()
        {
            // Assert
            Assert.IsNotNull(TestRootDirectory, "Test root directory should be initialized");
            Assert.IsTrue(Directory.Exists(TestRootDirectory), "Test root directory should exist");
            Assert.IsNotNull(FileSystemHelper, "File system helper should be initialized");
            Assert.IsNotNull(PerformanceHelper, "Performance helper should be initialized");

            // Verify test isolation - each test should have unique directory
            Assert.IsTrue(TestRootDirectory.Contains("ClutterFlockIntegrationTests"), 
                "Test directory should be in integration test folder");
        }
    }
}