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
        public StorageScheduleSnapshot? LastSchedule { get; private set; }

        public FolderScanner(ICacheManager cacheManager, IErrorRecoveryService errorRecoveryService)
        {
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _errorRecoveryService = errorRecoveryService ?? throw new ArgumentNullException(nameof(errorRecoveryService));
        }

        public Task<List<string>> ScanFolderHierarchyAsync(string rootPath, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
            => ScanFoldersAsync([rootPath], progress, cancellationToken);

        public async Task<List<string>> ScanFoldersAsync(IReadOnlyList<string> roots, IProgress<AnalysisProgress>? progress,
            CancellationToken cancellationToken, StorageTopology? topology = null)
        {
            foreach (var root in roots)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(root);
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Directory not found: {root}");
            }
            topology ??= StorageTopology.Discover(roots);
            var scheduler = new AdaptiveStorageScheduler(topology.Devices, availableProcessors: topology.AvailableProcessors);
            var discovered = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            var completed = 0;
            void Queue(string path)
            {
                path = PathUtilities.Normalize(path);
                if (!discovered.TryAdd(path, 0)) return;
                scheduler.Enqueue(topology.Resolve(path), async (work, token) =>
                {
                    // Discover children and scan metadata in the same pass. All roots enter the
                    // device queues together; a slow root cannot prevent another disk starting.
                    try
                    {
                        foreach (var child in new DirectoryInfo(path).EnumerateDirectories())
                        {
                            token.ThrowIfCancellationRequested();
                            if ((child.Attributes & FileAttributes.ReparsePoint) == 0) Queue(child.FullName);
                            work.Report(1);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { _errorRecoveryService.LogSkippedItem(path, ex.Message); }
                    if (!_cacheManager.IsFolderCached(path))
                        _cacheManager.CacheFolderInfo(path, await AnalyzeFolderCoreAsync(path, token, work.Report).ConfigureAwait(false));
                    work.Report(1); Interlocked.Increment(ref completed);
                });
            }
            foreach (var root in roots) Queue(root);
            void Report(StorageScheduleSnapshot state)
            {
                LastSchedule = state;
                progress?.Report(new AnalysisProgress { Phase = AnalysisPhase.ScanningFolders,
                    CurrentProgress = Volatile.Read(ref completed), MaxProgress = discovered.Count, IsIndeterminate = true,
                    StatusMessage = $"Scanning folders: {Volatile.Read(ref completed):N0}/{discovered.Count:N0} discovered · " +
                        $"{state.Devices.Sum(d => d.UnitsPerSecond):N0} entries/s · {state.Summary}" });
            }
            Report(scheduler.Snapshot);
            await scheduler.RunAsync(cancellationToken, Report).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return discovered.Keys.ToList();
        }

        // Historical two-pass scanning annotations; discovery and metadata now share adaptive queues.
            // Phase 1: Count all subfolders with immediate progress reporting
                        // Report progress more frequently for better user feedback
            // Phase 2: Scan folders that haven't been cached
            // Use Parallel.ForEachAsync to control parallelism.
            // Process folders in parallel with controlled concurrency.

        public Task<FolderInfo> AnalyzeFolderAsync(string folderPath, CancellationToken cancellationToken)
            => AnalyzeFolderCoreAsync(folderPath, cancellationToken, null);

        private async Task<FolderInfo> AnalyzeFolderCoreAsync(string folderPath, CancellationToken cancellationToken, Action<long>? report)
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
                    foreach (var info in new DirectoryInfo(folderPath).EnumerateFiles())
                    {
                        var file = info.FullName;
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            // Enumeration already supplies size and timestamps; avoid a second disk lookup.
                            var metadata = new FileMetadata
                            {
                                FileName = info.Name,
                                Size = info.Length,
                                LastWriteTime = info.LastWriteTime
                            };
                            _cacheManager.CacheFileMetadata(file, metadata);
                            report?.Invoke(1);
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
