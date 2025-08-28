using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Integration
{
    /// <summary>
    /// Base class for integration tests that provides common setup/teardown functionality
    /// for real file system testing and component integration scenarios.
    /// </summary>
    [TestClass]
    public abstract class IntegrationTestBase
    {
        protected string TestRootDirectory { get; private set; } = string.Empty;
        protected TestFileSystemHelper FileSystemHelper { get; private set; } = null!;
        protected PerformanceTestHelper PerformanceHelper { get; private set; } = null!;

        [TestInitialize]
        public virtual async Task SetupAsync()
        {
            // Create unique test directory for this test run
            TestRootDirectory = Path.Combine(Path.GetTempPath(), "ClutterFlockIntegrationTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(TestRootDirectory);

            // Initialize test helpers
            FileSystemHelper = new TestFileSystemHelper(TestRootDirectory);
            PerformanceHelper = new PerformanceTestHelper();

            // Allow derived classes to perform additional setup
            await OnSetupAsync();
        }

        [TestCleanup]
        public virtual async Task CleanupAsync()
        {
            try
            {
                // Allow derived classes to perform cleanup first
                await OnCleanupAsync();

                // Clean up performance monitoring
                PerformanceHelper?.Dispose();

                // Clean up test directory
                if (Directory.Exists(TestRootDirectory))
                {
                    Directory.Delete(TestRootDirectory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                // Log cleanup errors but don't fail the test
                Console.WriteLine($"Warning: Cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Override this method in derived classes to perform additional setup
        /// </summary>
        protected virtual Task OnSetupAsync() => Task.CompletedTask;

        /// <summary>
        /// Override this method in derived classes to perform additional cleanup
        /// </summary>
        protected virtual Task OnCleanupAsync() => Task.CompletedTask;

        /// <summary>
        /// Creates a test folder structure with the specified configuration
        /// </summary>
        protected async Task<string> CreateTestFolderStructureAsync(TestFileStructure structure)
        {
            return await FileSystemHelper.CreateTestStructureAsync(structure);
        }

        /// <summary>
        /// Validates that a directory exists and contains expected files
        /// </summary>
        protected void AssertDirectoryExists(string path, int expectedFileCount = -1)
        {
            Assert.IsTrue(Directory.Exists(path), $"Directory should exist: {path}");
            
            if (expectedFileCount >= 0)
            {
                var actualFileCount = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
                Assert.AreEqual(expectedFileCount, actualFileCount, 
                    $"Directory should contain {expectedFileCount} files but contains {actualFileCount}");
            }
        }

        /// <summary>
        /// Validates that a file exists and has expected properties
        /// </summary>
        protected void AssertFileExists(string path, long expectedSize = -1)
        {
            Assert.IsTrue(File.Exists(path), $"File should exist: {path}");
            
            if (expectedSize >= 0)
            {
                var actualSize = new FileInfo(path).Length;
                Assert.AreEqual(expectedSize, actualSize, 
                    $"File should be {expectedSize} bytes but is {actualSize} bytes");
            }
        }
    }
}