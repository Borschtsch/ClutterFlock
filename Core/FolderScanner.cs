using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Core
{
    /// <summary>
    /// Implementation of folder scanning operations
    /// </summary>
    public class FolderScanner : IFolderScanner
    {
        private readonly ICacheManager _cacheManager;
        private readonly IErrorRecoveryService _errorRecoveryService;
        private static readonly int MaxParallelism = Math.Max(1, Environment.ProcessorCount - 1);

        public FolderScanner(ICacheManager cacheManager, IErrorRecoveryService errorRecoveryService)
        {
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _errorRecoveryService = errorRecoveryService ?? throw new ArgumentNullException(nameof(errorRecoveryService));
        }

        public async Task<List<string>> ScanFolderHierarchyAsync(string rootPath, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Path cannot be empty or whitespace.", nameof(rootPath));
            if (!Directory.Exists(rootPath)) throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

            var subfolders = new List<string>();
            var stack = new Stack<string>();
            stack.Push(rootPath);

            // Phase 1: Count all subfolders with immediate progress reporting
            progress?.Report(new AnalysisProgress 
            { 
                Phase = AnalysisPhase.CountingFolders, 
                StatusMessage = "Counting subfolders...", 
                IsIndeterminate = true 
            });

            await Task.Run(() =>
            {
                while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
                {
                    var current = stack.Pop();
                    subfolders.Add(current);

                    try
                    {
                        foreach (var dir in Directory.GetDirectories(current))
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            stack.Push(dir);
                        }

                        // Report progress more frequently for better user feedback
                        if (subfolders.Count % 100 == 0 || subfolders.Count == 1)
                        {
                            progress?.Report(new AnalysisProgress
                            {
                                Phase = AnalysisPhase.CountingFolders,
                                StatusMessage = $"Found {subfolders.Count} subfolders...",
                                IsIndeterminate = true
                            });
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        _errorRecoveryService.LogSkippedItem(current, "Access denied");
                        continue;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        _errorRecoveryService.LogSkippedItem(current, "Directory not found");
                        continue;
                    }
                    catch (IOException ex)
                    {
                        _errorRecoveryService.LogSkippedItem(current, $"IO error: {ex.Message}");
                        continue;
                    }
                }
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Phase 2: Scan folders that haven't been cached
            var foldersToScan = subfolders.Where(folder => !_cacheManager.IsFolderCached(folder)).ToList();
            
            if (foldersToScan.Count == 0)
            {
                progress?.Report(new AnalysisProgress
                {
                    Phase = AnalysisPhase.Complete,
                    StatusMessage = "All folders already scanned",
                    CurrentProgress = subfolders.Count,
                    MaxProgress = subfolders.Count
                });
                return subfolders;
            }

            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.ScanningFolders,
                StatusMessage = $"Scanning {foldersToScan.Count} new folders...",
                CurrentProgress = 0,
                MaxProgress = foldersToScan.Count
            });

            // Use SemaphoreSlim to control parallelism 
            using var semaphore = new SemaphoreSlim(MaxParallelism, MaxParallelism);
            int scannedCount = 0;

            // Process folders in parallel with controlled concurrency
            var semaphoreTasks = foldersToScan.Select(async folder =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var folderInfo = await AnalyzeFolderAsync(folder, cancellationToken);
                    _cacheManager.CacheFolderInfo(folder, folderInfo);

                    var currentCount = Interlocked.Increment(ref scannedCount);
                    if (currentCount % 25 == 0 || currentCount == foldersToScan.Count)
                    {
                        progress?.Report(new AnalysisProgress
                        {
                            Phase = AnalysisPhase.ScanningFolders,
                            StatusMessage = $"Building file index: {currentCount}/{foldersToScan.Count} folders...",
                            CurrentProgress = currentCount,
                            MaxProgress = foldersToScan.Count
                        });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Handle folder scanning errors with recovery service
                    var recoveryAction = await _errorRecoveryService.HandleFileAccessError(folder, ex);
                    _errorRecoveryService.LogSkippedItem(folder, $"Folder scan failed: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(semaphoreTasks);

            return subfolders;
        }

        public async Task<FolderInfo> AnalyzeFolderAsync(string folderPath, CancellationToken cancellationToken)
        {
            if (folderPath == null) throw new ArgumentNullException(nameof(folderPath));
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("Path cannot be empty or whitespace.", nameof(folderPath));
            if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

            return await Task.Run(() => AnalyzeFolderSync(folderPath), cancellationToken);
        }

        private FolderInfo AnalyzeFolderSync(string folderPath)
        {
            try
            {
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
                var totalSize = 0L;
                var latestDate = DateTime.MinValue;

                // Cache individual file metadata to avoid future file system access
                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        var metadata = new FileMetadata
                        {
                            FileName = info.Name,
                            Size = info.Length,
                            LastWriteTime = info.LastWriteTime
                        };
                        
                        _cacheManager.CacheFileMetadata(file, metadata);
                        totalSize += info.Length;
                        
                        if (info.LastWriteTime > latestDate)
                            latestDate = info.LastWriteTime;
                    }
                    catch (Exception ex)
                    {
                        // Use error recovery service for file access errors
                        var recoveryAction = _errorRecoveryService.HandleFileAccessError(file, ex).Result;
                        _errorRecoveryService.LogSkippedItem(file, $"File access failed: {ex.Message}");
                        continue;
                    }
                }

                return new FolderInfo
                {
                    Files = files.ToList(),
                    TotalSize = totalSize,
                    LatestModificationDate = latestDate == DateTime.MinValue ? null : latestDate
                };
            }
            catch
            {
                return new FolderInfo(); // Return empty info for inaccessible folders
            }
        }

        public int CountSubfolders(string rootPath)
        {
            if (rootPath == null) throw new ArgumentNullException(nameof(rootPath));
            if (string.IsNullOrWhiteSpace(rootPath)) throw new ArgumentException("Path cannot be empty or whitespace.", nameof(rootPath));
            if (!Directory.Exists(rootPath)) throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

            try
            {
                var count = 0;
                var stack = new Stack<string>();
                stack.Push(rootPath);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    count++;

                    try
                    {
                        foreach (var dir in Directory.GetDirectories(current))
                            stack.Push(dir);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip inaccessible directories in counting phase
                        continue;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Skip missing directories in counting phase
                        continue;
                    }
                }

                return count;
            }
            catch
            {
                return 1; // At least the root folder itself
            }
        }
    }
}
