using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Tests.TestUtilities;

namespace ClutterFlock.Tests.Unit.Core
{
    [TestClass]
    [TestCategory(TestCategories.Unit)]
    public class ErrorRecoveryServiceTests : TestBase
    {
        private ErrorRecoveryService _errorRecoveryService = null!;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _errorRecoveryService = new ErrorRecoveryService();
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithUnauthorizedAccessException_ReturnsRetryWithElevationAction()
        {
            // Arrange
            var filePath = @"C:\TestFolder\restricted.txt";
            var error = new UnauthorizedAccessException("Access denied");

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.RetryWithElevation, result.Type);
            Assert.IsFalse(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithDirectoryNotFoundException_ReturnsSkipAction()
        {
            // Arrange
            var filePath = @"C:\NonExistentFolder\file.txt";
            var error = new DirectoryNotFoundException("Directory not found");

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Skip, result.Type);
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithFileNotFoundException_ReturnsSkipAction()
        {
            // Arrange
            var filePath = @"C:\TestFolder\missing.txt";
            var error = new FileNotFoundException("File not found");

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Skip, result.Type);
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithIOExceptionFileLocked_ReturnsRetryAction()
        {
            // Arrange
            var filePath = @"C:\TestFolder\locked.txt";
            // Create an IOException with the specific HResult for sharing violation
            var error = new IOException("The process cannot access the file because it is being used by another process");
            // Set the HResult to simulate ERROR_SHARING_VIOLATION (32)
            var hresultField = typeof(Exception).GetField("_HResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            hresultField?.SetValue(error, unchecked((int)0x80070020)); // HRESULT for sharing violation

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Retry, result.Type);
            Assert.IsTrue(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithIOExceptionNotFileLocked_ReturnsSkipAction()
        {
            // Arrange
            var filePath = @"C:\TestFolder\file.txt";
            var error = new IOException("Generic IO error");

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Skip, result.Type);
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithPathTooLongException_ReturnsSkipAction()
        {
            // Arrange
            var longPath = @"C:\" + new string('a', 300) + @"\file.txt";
            var error = new PathTooLongException("Path too long");

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(longPath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Skip, result.Type);
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithGenericException_ReturnsSkipAction()
        {
            // Arrange
            var filePath = @"C:\TestFolder\file.txt";
            var error = new InvalidOperationException("Generic error");

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Skip, result.Type);
        }

        [TestMethod]
        public async Task HandleNetworkError_WithNetworkPath_ReturnsPauseAndWaitAction()
        {
            // Arrange
            var networkPath = @"\\server\share\file.txt";
            var error = new IOException("Network error");

            // Act
            var result = await _errorRecoveryService.HandleNetworkError(networkPath, error);

            // Assert
            Assert.IsNotNull(result);
            // The method checks network reachability, and since the server doesn't exist, it returns PauseAndWait
            Assert.AreEqual(RecoveryActionType.PauseAndWait, result.Type);
            Assert.IsTrue(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithMemoryConstraint_ReturnsReduceParallelismAction()
        {
            // Arrange
            var error = new OutOfMemoryException("Out of memory");

            // Act
            var result = await _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.Memory, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.ReduceParallelism, result.Type);
            Assert.IsTrue(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithDiskSpaceConstraint_ReturnsAbortAction()
        {
            // Arrange
            var error = new IOException("Disk full");

            // Act
            var result = await _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.DiskSpace, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Abort, result.Type);
            Assert.IsFalse(result.ShouldRetry);
        }

        [TestMethod]
        public void LogSkippedItem_AddsItemToSummary()
        {
            // Arrange
            var path = @"C:\TestFolder\skipped.txt";
            var reason = "Access denied";

            // Act
            _errorRecoveryService.LogSkippedItem(path, reason);

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(1, summary.SkippedFiles);
            Assert.IsTrue(summary.SkippedPaths.Contains(path));
        }

        [TestMethod]
        public async Task GetErrorSummary_AfterMultipleErrors_ReturnsCorrectCounts()
        {
            // Arrange
            var filePath1 = @"C:\TestFolder\file1.txt";
            var filePath2 = @"C:\TestFolder\file2.txt";
            var networkPath = @"\\server\share\file.txt";

            // Act
            await _errorRecoveryService.HandleFileAccessError(filePath1, new UnauthorizedAccessException("Access denied"));
            await _errorRecoveryService.HandleFileAccessError(filePath2, new FileNotFoundException("File not found"));
            await _errorRecoveryService.HandleNetworkError(networkPath, new IOException("Network error"));
            _errorRecoveryService.LogSkippedItem(filePath1, "Access denied");
            _errorRecoveryService.LogSkippedItem(filePath2, "File not found");

            var summary = _errorRecoveryService.GetErrorSummary();

            // Assert
            Assert.AreEqual(1, summary.PermissionErrors); // Only UnauthorizedAccessException increments this
            Assert.AreEqual(1, summary.NetworkErrors);
            Assert.AreEqual(2, summary.SkippedFiles);
            Assert.IsTrue(summary.ErrorMessages.Count >= 5); // Each operation adds a message
            Assert.IsTrue(summary.LastErrorTime > DateTime.MinValue);
        }

        [TestMethod]
        public void ClearErrorSummary_ResetsAllCounters()
        {
            // Arrange
            _errorRecoveryService.LogSkippedItem(@"C:\test.txt", "Test reason");

            // Act
            _errorRecoveryService.ClearErrorSummary();

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(0, summary.PermissionErrors);
            Assert.AreEqual(0, summary.NetworkErrors);
            Assert.AreEqual(0, summary.ResourceErrors);
            Assert.AreEqual(0, summary.SkippedFiles);
            Assert.AreEqual(0, summary.ErrorMessages.Count);
            Assert.AreEqual(0, summary.SkippedPaths.Count);
        }

        [TestMethod]
        public async Task HandleFileAccessError_UpdatesErrorSummary()
        {
            // Arrange
            var filePath = @"C:\TestFolder\file.txt";
            var error = new UnauthorizedAccessException("Access denied");

            // Act
            await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(1, summary.PermissionErrors);
            Assert.IsTrue(summary.ErrorMessages.Count > 0);
            Assert.IsTrue(summary.LastErrorTime > DateTime.MinValue);
        }

        [TestMethod]
        public async Task HandleNetworkError_UpdatesErrorSummary()
        {
            // Arrange
            var networkPath = @"\\server\share\file.txt";
            var error = new IOException("Network timeout");

            // Act
            await _errorRecoveryService.HandleNetworkError(networkPath, error);

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(1, summary.NetworkErrors);
            Assert.IsTrue(summary.ErrorMessages.Count > 0);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_UpdatesErrorSummary()
        {
            // Arrange
            var error = new OutOfMemoryException("Out of memory");

            // Act
            await _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.Memory, error);

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(1, summary.ResourceErrors);
            Assert.IsTrue(summary.ErrorMessages.Count > 0);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithFileHandles_ReturnsReduceParallelismAction()
        {
            // Arrange
            var error = new IOException("Too many open files");

            // Act
            var result = await _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.FileHandles, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.ReduceParallelism, result.Type);
            Assert.IsTrue(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithCpuUsage_ReturnsReduceParallelismAction()
        {
            // Arrange
            var error = new InvalidOperationException("CPU usage too high");

            // Act
            var result = await _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.CpuUsage, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.ReduceParallelism, result.Type);
            Assert.IsTrue(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithNetworkBandwidth_ReturnsPauseAndWaitAction()
        {
            // Arrange
            var error = new IOException("Network bandwidth exceeded");

            // Act
            var result = await _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.NetworkBandwidth, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.PauseAndWait, result.Type);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithUnknownType_ReturnsReduceParallelismAction()
        {
            // Arrange
            var error = new Exception("Unknown constraint");

            // Act
            var result = await _errorRecoveryService.HandleResourceConstraintError((ResourceConstraintType)999, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.ReduceParallelism, result.Type);
            Assert.IsTrue(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleNetworkError_WithLocalPath_ReturnsRetryAction()
        {
            // Arrange
            var localPath = @"C:\TestFolder\file.txt";
            var error = new IOException("Network error");

            // Act
            var result = await _errorRecoveryService.HandleNetworkError(localPath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Retry, result.Type);
        }

        [TestMethod]
        public void LogSkippedItem_MultipleItems_UpdatesCountCorrectly()
        {
            // Arrange & Act
            _errorRecoveryService.LogSkippedItem(@"C:\file1.txt", "Reason 1");
            _errorRecoveryService.LogSkippedItem(@"C:\file2.txt", "Reason 2");
            _errorRecoveryService.LogSkippedItem(@"C:\file3.txt", "Reason 3");

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(3, summary.SkippedFiles);
            Assert.AreEqual(3, summary.SkippedPaths.Count);
            Assert.IsTrue(summary.SkippedPaths.Contains(@"C:\file1.txt"));
            Assert.IsTrue(summary.SkippedPaths.Contains(@"C:\file2.txt"));
            Assert.IsTrue(summary.SkippedPaths.Contains(@"C:\file3.txt"));
        }

        [TestMethod]
        public async Task ThreadSafety_ConcurrentOperations_NoExceptions()
        {
            // Arrange
            const int threadCount = 10;
            const int operationsPerThread = 50;
            var tasks = new List<Task>();

            // Act
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks.Add(Task.Run(async () =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var filePath = $@"C:\Thread{threadId}\file{i}.txt";
                        await _errorRecoveryService.HandleFileAccessError(filePath, new UnauthorizedAccessException("Test"));
                        _errorRecoveryService.LogSkippedItem(filePath, $"Thread {threadId} operation {i}");
                    }
                }));
            }

            // Assert - No exceptions should be thrown
            AssertCompletesWithinTime(() => Task.WaitAll(tasks.ToArray()), TimeSpan.FromSeconds(30));
            
            // Verify final state
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(threadCount * operationsPerThread, summary.PermissionErrors);
            Assert.AreEqual(threadCount * operationsPerThread, summary.SkippedFiles);
        }
    }
}