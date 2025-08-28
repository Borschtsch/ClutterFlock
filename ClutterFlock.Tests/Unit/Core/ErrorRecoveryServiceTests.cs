using System;
using System.IO;
using System.Linq;
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
            Assert.IsFalse(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithNullFilePath_ThrowsArgumentNullException()
        {
            // Arrange
            var error = new IOException("Test error");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _errorRecoveryService.HandleFileAccessError(null!, error));
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithNullError_ThrowsArgumentNullException()
        {
            // Arrange
            var filePath = @"C:\TestFile.txt";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _errorRecoveryService.HandleFileAccessError(filePath, null!));
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithIOException_ReturnsRetryAction()
        {
            // Arrange
            var filePath = @"C:\TestFile.txt";
            // Create IOException with ERROR_SHARING_VIOLATION (32) to simulate file lock
            var error = new IOException("The process cannot access the file because it is being used by another process.", 32);

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Retry, result.Type);
            Assert.IsTrue(result.ShouldRetry);
            Assert.IsTrue(result.RetryDelay > TimeSpan.Zero);
        }

        [TestMethod]
        public async Task HandleFileAccessError_WithGenericException_ReturnsSkipAction()
        {
            // Arrange
            var filePath = @"C:\TestFile.txt";
            var error = new InvalidOperationException("Generic error");

            // Act
            var result = await _errorRecoveryService.HandleFileAccessError(filePath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Skip, result.Type);
            Assert.IsFalse(result.ShouldRetry);
        }

        [TestMethod]
        public async Task HandleNetworkError_WithNullPath_ThrowsArgumentNullException()
        {
            // Arrange
            var error = new IOException("Network error");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _errorRecoveryService.HandleNetworkError(null!, error));
        }

        [TestMethod]
        public async Task HandleNetworkError_WithNullError_ThrowsArgumentNullException()
        {
            // Arrange
            var networkPath = @"\\server\share\file.txt";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _errorRecoveryService.HandleNetworkError(networkPath, null!));
        }

        [TestMethod]
        public async Task HandleNetworkError_WithNetworkPath_ReturnsRetryAction()
        {
            // Arrange
            var networkPath = @"C:\LocalPath\file.txt"; // Use local path to avoid network reachability check
            var error = new IOException("Network path not found");

            // Act
            var result = await _errorRecoveryService.HandleNetworkError(networkPath, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.Retry, result.Type);
            Assert.IsTrue(result.ShouldRetry);
            Assert.IsTrue(result.RetryDelay > TimeSpan.Zero);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithNullError_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.Memory, null!));
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
        public async Task HandleResourceConstraintError_WithDiskSpaceConstraint_ReturnsPauseAndWaitAction()
        {
            // Arrange
            var error = new IOException("Disk full");

            // Act
            var result = await _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.DiskSpace, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.PauseAndWait, result.Type);
            Assert.IsTrue(result.ShouldRetry);
            Assert.IsTrue(result.RetryDelay > TimeSpan.Zero);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithFileHandlesConstraint_ReturnsReduceParallelismAction()
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
        public void LogSkippedItem_WithNullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                _errorRecoveryService.LogSkippedItem(null!, "reason"));
        }

        [TestMethod]
        public void LogSkippedItem_WithNullReason_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                _errorRecoveryService.LogSkippedItem(@"C:\path", null!));
        }

        [TestMethod]
        public void LogSkippedItem_WithValidParameters_LogsItem()
        {
            // Arrange
            var path = @"C:\TestFile.txt";
            var reason = "Access denied";

            // Act
            _errorRecoveryService.LogSkippedItem(path, reason);

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(1, summary.SkippedFiles);
            Assert.IsTrue(summary.SkippedPaths.Contains(path));
            Assert.IsTrue(summary.ErrorMessages.Any(msg => msg.Contains(reason)));
        }

        [TestMethod]
        public void GetErrorSummary_InitialState_ReturnsEmptySummary()
        {
            // Act
            var summary = _errorRecoveryService.GetErrorSummary();

            // Assert
            Assert.IsNotNull(summary);
            Assert.AreEqual(0, summary.SkippedFiles);
            Assert.AreEqual(0, summary.PermissionErrors);
            Assert.AreEqual(0, summary.NetworkErrors);
            Assert.AreEqual(0, summary.ResourceErrors);
            Assert.IsFalse(summary.HasErrors);
            Assert.AreEqual(0, summary.TotalErrors);
        }

        [TestMethod]
        public void ClearErrorSummary_AfterLoggingErrors_ClearsAllErrors()
        {
            // Arrange
            _errorRecoveryService.LogSkippedItem(@"C:\test1.txt", "reason1");
            _errorRecoveryService.LogSkippedItem(@"C:\test2.txt", "reason2");

            // Act
            _errorRecoveryService.ClearErrorSummary();

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(0, summary.SkippedFiles);
            Assert.AreEqual(0, summary.ErrorMessages.Count);
            Assert.AreEqual(0, summary.SkippedPaths.Count);
            Assert.IsFalse(summary.HasErrors);
        }

        [TestMethod]
        public void LogSkippedItem_MultipleItems_AccumulatesCorrectly()
        {
            // Arrange & Act
            _errorRecoveryService.LogSkippedItem(@"C:\test1.txt", "reason1");
            _errorRecoveryService.LogSkippedItem(@"C:\test2.txt", "reason2");
            _errorRecoveryService.LogSkippedItem(@"C:\test3.txt", "reason3");

            // Assert
            var summary = _errorRecoveryService.GetErrorSummary();
            Assert.AreEqual(3, summary.SkippedFiles);
            Assert.AreEqual(3, summary.ErrorMessages.Count);
            Assert.AreEqual(3, summary.SkippedPaths.Count);
            Assert.IsTrue(summary.HasErrors);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithNetworkBandwidthConstraint_ReturnsPauseAndWaitAction()
        {
            // Arrange
            var error = new IOException("Network bandwidth exceeded");

            // Act
            var result = await _errorRecoveryService.HandleResourceConstraintError(ResourceConstraintType.NetworkBandwidth, error);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(RecoveryActionType.PauseAndWait, result.Type);
            Assert.IsTrue(result.ShouldRetry);
            Assert.IsTrue(result.RetryDelay > TimeSpan.Zero);
        }

        [TestMethod]
        public async Task HandleResourceConstraintError_WithCpuUsageConstraint_ReturnsReduceParallelismAction()
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
            Assert.IsFalse(result.ShouldRetry);
        }
    }
}