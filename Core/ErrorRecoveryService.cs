using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Core
{
    /// <summary>
    /// Service for comprehensive error recovery and management during file operations
    /// </summary>
    public class ErrorRecoveryService : IErrorRecoveryService
    {
        private readonly ErrorSummary _errorSummary;
        private readonly object _lockObject = new object();

        public ErrorRecoveryService()
        {
            _errorSummary = new ErrorSummary();
        }

        /// <summary>
        /// Handles file access errors with appropriate recovery actions
        /// </summary>
        public async Task<RecoveryAction> HandleFileAccessError(string filePath, Exception error)
        {
            lock (_lockObject)
            {
                _errorSummary.LastErrorTime = DateTime.Now;
                _errorSummary.ErrorMessages.Add($"File access error: {filePath} - {error.Message}");
            }

            return error switch
            {
                UnauthorizedAccessException => await HandleUnauthorizedAccess(filePath, error),
                DirectoryNotFoundException => HandleDirectoryNotFound(filePath, error),
                FileNotFoundException => HandleFileNotFound(filePath, error),
                IOException ioEx when IsFileLocked(ioEx) => HandleFileLocked(filePath, error),
                IOException ioEx when IsNetworkPath(filePath) => await HandleNetworkError(filePath, error),
                PathTooLongException => HandlePathTooLong(filePath, error),
                _ => HandleGenericFileError(filePath, error)
            };
        }

        /// <summary>
        /// Handles network-related errors with retry logic
        /// </summary>
        public async Task<RecoveryAction> HandleNetworkError(string networkPath, Exception error)
        {
            lock (_lockObject)
            {
                _errorSummary.NetworkErrors++;
                _errorSummary.LastErrorTime = DateTime.Now;
                _errorSummary.ErrorMessages.Add($"Network error: {networkPath} - {error.Message}");
            }

            // Check if network path is accessible
            if (IsNetworkPath(networkPath))
            {
                var isReachable = await IsNetworkPathReachable(networkPath);
                if (!isReachable)
                {
                    return new RecoveryAction
                    {
                        Type = RecoveryActionType.PauseAndWait,
                        Message = $"Network path '{networkPath}' is not reachable",
                        SuggestedSolution = "Check network connectivity and ensure the network drive is accessible. The operation will retry automatically.",
                        ShouldRetry = true,
                        RetryDelay = TimeSpan.FromSeconds(30)
                    };
                }
            }

            return new RecoveryAction
            {
                Type = RecoveryActionType.Retry,
                Message = $"Network error accessing '{networkPath}'",
                SuggestedSolution = "The network connection may be temporarily unavailable. Retrying operation.",
                ShouldRetry = true,
                RetryDelay = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>
        /// Handles resource constraint errors with optimization suggestions
        /// </summary>
        public async Task<RecoveryAction> HandleResourceConstraintError(ResourceConstraintType type, Exception error)
        {
            lock (_lockObject)
            {
                _errorSummary.ResourceErrors++;
                _errorSummary.LastErrorTime = DateTime.Now;
                _errorSummary.ErrorMessages.Add($"Resource constraint ({type}): {error.Message}");
            }

            return type switch
            {
                ResourceConstraintType.Memory => await HandleMemoryConstraint(error),
                ResourceConstraintType.DiskSpace => HandleDiskSpaceConstraint(error),
                ResourceConstraintType.FileHandles => HandleFileHandleConstraint(error),
                ResourceConstraintType.CpuUsage => HandleCpuConstraint(error),
                ResourceConstraintType.NetworkBandwidth => HandleNetworkBandwidthConstraint(error),
                _ => HandleGenericResourceConstraint(type, error)
            };
        }

        /// <summary>
        /// Logs a skipped item with reason
        /// </summary>
        public void LogSkippedItem(string path, string reason)
        {
            lock (_lockObject)
            {
                _errorSummary.SkippedFiles++;
                _errorSummary.SkippedPaths.Add(path);
                _errorSummary.ErrorMessages.Add($"Skipped: {path} - {reason}");
                _errorSummary.LastErrorTime = DateTime.Now;
            }
        }

        /// <summary>
        /// Gets the current error summary
        /// </summary>
        public ErrorSummary GetErrorSummary()
        {
            lock (_lockObject)
            {
                return new ErrorSummary
                {
                    SkippedFiles = _errorSummary.SkippedFiles,
                    PermissionErrors = _errorSummary.PermissionErrors,
                    NetworkErrors = _errorSummary.NetworkErrors,
                    ResourceErrors = _errorSummary.ResourceErrors,
                    SkippedPaths = new List<string>(_errorSummary.SkippedPaths),
                    ErrorMessages = new List<string>(_errorSummary.ErrorMessages),
                    LastErrorTime = _errorSummary.LastErrorTime
                };
            }
        }

        /// <summary>
        /// Clears the error summary
        /// </summary>
        public void ClearErrorSummary()
        {
            lock (_lockObject)
            {
                _errorSummary.SkippedFiles = 0;
                _errorSummary.PermissionErrors = 0;
                _errorSummary.NetworkErrors = 0;
                _errorSummary.ResourceErrors = 0;
                _errorSummary.SkippedPaths.Clear();
                _errorSummary.ErrorMessages.Clear();
                _errorSummary.LastErrorTime = default;
            }
        }

        #region Private Helper Methods

        private Task<RecoveryAction> HandleUnauthorizedAccess(string filePath, Exception error)
        {
            lock (_lockObject)
            {
                _errorSummary.PermissionErrors++;
            }

            return Task.FromResult(new RecoveryAction
            {
                Type = RecoveryActionType.RetryWithElevation,
                Message = $"Access denied to '{filePath}'",
                SuggestedSolution = "Try running the application as Administrator, or check file/folder permissions. The file will be skipped for now.",
                ShouldRetry = false,
                RetryDelay = TimeSpan.Zero
            });
        }

        private RecoveryAction HandleDirectoryNotFound(string filePath, Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.Skip,
                Message = $"Directory not found: '{filePath}'",
                SuggestedSolution = "The directory may have been moved or deleted. Skipping this location.",
                ShouldRetry = false,
                RetryDelay = TimeSpan.Zero
            };
        }

        private RecoveryAction HandleFileNotFound(string filePath, Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.Skip,
                Message = $"File not found: '{filePath}'",
                SuggestedSolution = "The file may have been moved or deleted during analysis. Skipping this file.",
                ShouldRetry = false,
                RetryDelay = TimeSpan.Zero
            };
        }

        private RecoveryAction HandleFileLocked(string filePath, Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.Retry,
                Message = $"File is locked or in use: '{filePath}'",
                SuggestedSolution = "The file is currently being used by another application. Will retry after a short delay.",
                ShouldRetry = true,
                RetryDelay = TimeSpan.FromSeconds(2)
            };
        }

        private RecoveryAction HandlePathTooLong(string filePath, Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.Skip,
                Message = $"Path too long: '{filePath}'",
                SuggestedSolution = "The file path exceeds Windows path length limits. Consider moving files to shorter paths.",
                ShouldRetry = false,
                RetryDelay = TimeSpan.Zero
            };
        }

        private RecoveryAction HandleGenericFileError(string filePath, Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.Skip,
                Message = $"File error: '{filePath}' - {error.Message}",
                SuggestedSolution = "An unexpected file system error occurred. The file will be skipped.",
                ShouldRetry = false,
                RetryDelay = TimeSpan.Zero
            };
        }

        private async Task<RecoveryAction> HandleMemoryConstraint(Exception error)
        {
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            await Task.Delay(1000); // Give system time to free memory

            return new RecoveryAction
            {
                Type = RecoveryActionType.ReduceParallelism,
                Message = "System memory usage is high",
                SuggestedSolution = "Reducing processing parallelism to conserve memory. Consider closing other applications.",
                ShouldRetry = true,
                RetryDelay = TimeSpan.FromSeconds(2)
            };
        }

        private RecoveryAction HandleDiskSpaceConstraint(Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.Abort,
                Message = "Insufficient disk space",
                SuggestedSolution = "Free up disk space and restart the analysis. The operation cannot continue.",
                ShouldRetry = false,
                RetryDelay = TimeSpan.Zero
            };
        }

        private RecoveryAction HandleFileHandleConstraint(Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.ReduceParallelism,
                Message = "Too many open file handles",
                SuggestedSolution = "Reducing parallelism to limit concurrent file operations.",
                ShouldRetry = true,
                RetryDelay = TimeSpan.FromSeconds(1)
            };
        }

        private RecoveryAction HandleCpuConstraint(Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.ReduceParallelism,
                Message = "High CPU usage detected",
                SuggestedSolution = "Reducing processing parallelism to prevent system overload.",
                ShouldRetry = true,
                RetryDelay = TimeSpan.FromSeconds(1)
            };
        }

        private RecoveryAction HandleNetworkBandwidthConstraint(Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.PauseAndWait,
                Message = "Network bandwidth constraint",
                SuggestedSolution = "Pausing to allow network congestion to clear.",
                ShouldRetry = true,
                RetryDelay = TimeSpan.FromSeconds(10)
            };
        }

        private RecoveryAction HandleGenericResourceConstraint(ResourceConstraintType type, Exception error)
        {
            return new RecoveryAction
            {
                Type = RecoveryActionType.ReduceParallelism,
                Message = $"Resource constraint: {type}",
                SuggestedSolution = "Reducing system load to address resource constraints.",
                ShouldRetry = true,
                RetryDelay = TimeSpan.FromSeconds(2)
            };
        }

        private static bool IsFileLocked(IOException ioException)
        {
            var errorCode = ioException.HResult & 0xFFFF;
            return errorCode == 32 || errorCode == 33; // ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION
        }

        private static bool IsNetworkPath(string path)
        {
            return path.StartsWith(@"\\") || 
                   (path.Length >= 2 && path[1] == ':' && IsMappedNetworkDrive(path[0]));
        }

        private static bool IsMappedNetworkDrive(char driveLetter)
        {
            try
            {
                var driveInfo = new DriveInfo(driveLetter.ToString());
                return driveInfo.DriveType == DriveType.Network;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> IsNetworkPathReachable(string networkPath)
        {
            try
            {
                // Extract server name from UNC path
                if (networkPath.StartsWith(@"\\"))
                {
                    var parts = networkPath.Substring(2).Split('\\');
                    if (parts.Length > 0)
                    {
                        var serverName = parts[0];
                        var ping = new Ping();
                        var reply = await ping.SendPingAsync(serverName, 5000);
                        return reply.Status == IPStatus.Success;
                    }
                }
                
                // For mapped drives, just check if directory exists
                return Directory.Exists(networkPath);
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}