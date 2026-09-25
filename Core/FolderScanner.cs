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
                            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0)
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

            // Use Parallel.ForEachAsync to control parallelism.
            int scannedCount = 0;
            // Process folders in parallel with controlled concurrency.
            await Parallel.ForEachAsync(foldersToScan, new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelism,
                CancellationToken = cancellationToken
            }, async (folder, token) =>
            {
                var folderInfo = await AnalyzeFolderAsync(folder, token);
                _cacheManager.CacheFolderInfo(folder, folderInfo);
                var current = Interlocked.Increment(ref scannedCount);
                progress?.Report(new AnalysisProgress
                {
                    Phase = AnalysisPhase.ScanningFolders,
                    StatusMessage = $"Scanning folders: {current}/{foldersToScan.Count}",
                    CurrentProgress = current,
                    MaxProgress = foldersToScan.Count
                });
            });

            return subfolders;
        }

        public async Task<FolderInfo> AnalyzeFolderAsync(string folderPath, CancellationToken cancellationToken)
        {
            if (folderPath == null) throw new ArgumentNullException(nameof(folderPath));
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("Path cannot be empty or whitespace.", nameof(folderPath));
            if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

            var result = new FolderInfo();
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    // Cache individual file metadata to avoid future file system access.
                    foreach (var file in Directory.EnumerateFiles(folderPath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
                            result.Files.Add(file);
                            result.TotalSize += metadata.Size;
                            if (result.LatestModificationDate == null || metadata.LastWriteTime > result.LatestModificationDate)
                                result.LatestModificationDate = metadata.LastWriteTime;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // Skip files that can't be accessed.
                            _errorRecoveryService.LogSkippedItem(file, ex.Message);
                        }
                    }
                    return result;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Handle folder scanning errors with recovery service.
                    var action = await _errorRecoveryService.HandleFileAccessError(folderPath, ex);
                    if (attempt == 0 && action.ShouldRetry)
                    {
                        await Task.Delay(action.RetryDelay, cancellationToken);
                        result = new FolderInfo();
                        continue;
                    }
                    _errorRecoveryService.LogSkippedItem(folderPath, ex.Message);
                    return result;
                }
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
                            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0)
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
