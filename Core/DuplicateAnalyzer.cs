using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Core
{
    /// <summary>
    /// Core duplicate analysis engine
    /// </summary>
    public partial class DuplicateAnalyzer : IDuplicateAnalyzer
    {
        private readonly ICacheManager _cacheManager;
        private readonly IErrorRecoveryService _errorRecoveryService;
        // CPU count is not a useful disk queue depth. Bound reads independently of hashing workers.
        // The former fixed limit was four. The read budget is now supplied by AnalysisOptions.
        private sealed class FileGroupComparer : IEqualityComparer<(string Name, long Size)>
        {
            public bool Equals((string Name, long Size) a, (string Name, long Size) b) => a.Size == b.Size && StringComparer.OrdinalIgnoreCase.Equals(a.Name, b.Name);
            public int GetHashCode((string Name, long Size) value) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name), value.Size);
        }

        public DuplicateAnalyzer(ICacheManager cacheManager, IErrorRecoveryService errorRecoveryService)
        {
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _errorRecoveryService = errorRecoveryService ?? throw new ArgumentNullException(nameof(errorRecoveryService));
        }

        public async Task<List<FileMatch>> FindDuplicateFilesAsync(List<string> folders, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken, Func<IReadOnlyList<FileMatch>, CancellationToken, Task>? matchesFound = null, AnalysisOptions? options = null)
        {
            options ??= new AnalysisOptions();
            if (options.MaxConcurrentReads < 0) throw new ArgumentOutOfRangeException(nameof(options.MaxConcurrentReads));
            // Phase 1: Organize cached file data for comparison
            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.BuildingFileIndex,
                StatusMessage = $"Organizing file data from {folders.Count} cached folders...",
                CurrentProgress = 0,
                MaxProgress = folders.Count
            });

            var fileNameSizeToFolders = await BuildFileIndexAsync(folders, progress, cancellationToken);
            
            cancellationToken.ThrowIfCancellationRequested();

            // Phase 2: Group potential duplicates
            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.BuildingFileIndex,
                StatusMessage = "Grouping potential duplicate files...",
                CurrentProgress = 0,
                MaxProgress = fileNameSizeToFolders.Count
            });

            var potentialDuplicateGroups = await GroupPotentialDuplicatesAsync(fileNameSizeToFolders, progress, cancellationToken);
            
            if (potentialDuplicateGroups.Count == 0)
            {
                progress?.Report(new AnalysisProgress
                {
                    Phase = AnalysisPhase.Complete,
                    StatusMessage = "No potential duplicate files found",
                    CurrentProgress = 0,
                    MaxProgress = 0
                });
                return new List<FileMatch>();
            }

            // Phase 3: Hash comparison
            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.ComparingFiles,
                StatusMessage = $"Comparing {potentialDuplicateGroups.Count:N0} potential duplicate groups...",
                CurrentProgress = 0,
                MaxProgress = potentialDuplicateGroups.Count
            });

            var duplicateMatches = await CompareFileHashesAsync(potentialDuplicateGroups, progress, cancellationToken, matchesFound, options);

            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.Complete,
                StatusMessage = $"Found {options.Statistics.FilePairsEmitted:N0} verified file pairs",
                CurrentProgress = duplicateMatches.Count,
                MaxProgress = duplicateMatches.Count
            });

            return duplicateMatches;
        }

        private async Task<Dictionary<(string, long), List<string>>> BuildFileIndexAsync(List<string> folders, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
        {
            var fileIndex = new Dictionary<(string, long), List<string>>(new FileGroupComparer());
            int processedFolders = 0;
            var indexClock = System.Diagnostics.Stopwatch.StartNew();

            // Process cached folder data (no actual file system scanning)
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var folderFiles = _cacheManager.GetFolderFiles(folder);
                int processedFiles = 0;
                
                foreach (var file in folderFiles)
                {
                    try
                    {
                        // Use cached metadata instead of accessing file system
                        var metadata = _cacheManager.GetFileMetadata(file);
                        if (metadata == null) continue;
                        
                        var key = (metadata.FileName, metadata.Size);
                        
                        if (!fileIndex.TryGetValue(key, out var list))
                            fileIndex[key] = list = new List<string>();
                        
                        list.Add(file);
                    }
                    catch (Exception ex)
                    {
                        // Use error recovery service for file access errors
                        var recoveryAction = await _errorRecoveryService.HandleFileAccessError(file, ex);
                        _errorRecoveryService.LogSkippedItem(file, $"File metadata access failed: {ex.Message}");
                        continue;
                    }

                    // Yield control every 100 files to keep UI responsive
                    // The former interval is now 4096: indexing runs off the dispatcher.
                    processedFiles++;
                    if (processedFiles % 4096 == 0)
                    {
                        await Task.Yield();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                var current = ++processedFolders;
                if (indexClock.ElapsedMilliseconds >= 250 || current == folders.Count)
                {
                    indexClock.Restart();
                    progress?.Report(new AnalysisProgress
                    {
                        Phase = AnalysisPhase.BuildingFileIndex,
                        StatusMessage = $"Organizing file data: {current}/{folders.Count} folders...",
                        CurrentProgress = current,
                        MaxProgress = folders.Count
                    });

                    // Allow UI updates
                    await Task.Yield();
                }
            }

            return fileIndex;
        }

        private Task<Dictionary<string, List<string>>> GroupPotentialDuplicatesAsync(Dictionary<(string, long), List<string>> fileIndex, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
        {
            // The direct path index replaces repeated walks through every matching folder.
            // Preserve the original grouping/worker annotations below for context.
            // Process each filename/size group that has multiple folders
                // Get all actual file paths for this filename/size combination
                        // Update progress more frequently - every 50 files OR every 10th group
                            // Allow UI updates more frequently
                // Always report progress after each group
                // Always yield after each group to keep UI responsive
            // Return only groups with multiple files (potential duplicates)
                            // Skip files in the same folder.
            cancellationToken.ThrowIfCancellationRequested();
            var groups = new Dictionary<string, List<string>>();
            foreach (var entry in fileIndex.Where(e => e.Value.Count > 1).OrderBy(e => e.Key.Item2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                groups.Add($"{entry.Key.Item1}_{entry.Key.Item2}", entry.Value);
            }
            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.BuildingFileIndex, StatusMessage = $"Indexed {groups.Count:N0} candidate groups; smaller files first.",
                CurrentProgress = groups.Count, MaxProgress = groups.Count
            });
            return Task.FromResult(groups);
        }

        // Empty matches remain evidence, but cannot qualify a folder pair on their own.
        internal static bool HasNonEmptyContent(FileMatch match, ICacheManager cache)
        {
            return HasContent(match.PathA) && HasContent(match.PathB);

            bool HasContent(string path)
            {
                var metadata = cache.GetFileMetadata(path);
                if (metadata != null) return metadata.Size > 0;
                // Legacy offline snapshots may have full hashes without file metadata.
                var hash = cache.GetFileHash(path);
                return hash is { Length: 64 } && hash.All(Uri.IsHexDigit)
                    && !hash.Equals("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task<List<FolderMatch>> AggregateFolderMatchesAsync(List<FileMatch> fileMatches, ICacheManager cacheManager, IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (fileMatches.Count == 0) return new List<FolderMatch>();

            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.AggregatingResults,
                StatusMessage = "Grouping file matches by folders...",
                IsIndeterminate = true
            });

            var folderGroups = fileMatches
                .Select(m => string.Compare(m.PathA, m.PathB, StringComparison.OrdinalIgnoreCase) <= 0
                    ? m : new FileMatch(m.PathB, m.PathA))
                .DistinctBy(m => (m.PathA.ToUpperInvariant(), m.PathB.ToUpperInvariant()))
                .GroupBy(m => (Path.GetDirectoryName(m.PathA) ?? string.Empty, Path.GetDirectoryName(m.PathB) ?? string.Empty))
                .Where(g => !string.IsNullOrEmpty(g.Key.Item1) && !string.IsNullOrEmpty(g.Key.Item2))
                .Where(g => g.Any(match => HasNonEmptyContent(match, cacheManager)))
                .ToList();

            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.AggregatingResults,
                StatusMessage = $"Creating folder matches for {folderGroups.Count:N0} folder pairs...",
                CurrentProgress = 0,
                MaxProgress = folderGroups.Count,
                IsIndeterminate = false
            });

            var folderMatches = new List<FolderMatch>();
            int processed = 0;

            foreach (var group in folderGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var leftFolder = group.Key.Item1;
                var rightFolder = group.Key.Item2;
                var duplicateFiles = group.ToList();

                var leftInfo = cacheManager.GetFolderInfo(leftFolder);
                var rightInfo = cacheManager.GetFolderInfo(rightFolder);

                var totalLeftFiles = leftInfo?.FileCount ?? 0;
                var totalRightFiles = rightInfo?.FileCount ?? 0;
                var folderSize = Math.Max(leftInfo?.TotalSize ?? 0, rightInfo?.TotalSize ?? 0);

                var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, 
                    totalLeftFiles, totalRightFiles, folderSize)
                {
                    LatestModificationDate = new[] { leftInfo?.LatestModificationDate, rightInfo?.LatestModificationDate }.Max()
                };

                folderMatches.Add(folderMatch);
                processed++;

                // Report progress and yield to UI thread every 25 folder pairs
                if (processed % 25 == 0 || processed == folderGroups.Count)
                {
                    progress?.Report(new AnalysisProgress
                    {
                        Phase = AnalysisPhase.AggregatingResults,
                        StatusMessage = $"Processed {processed:N0} of {folderGroups.Count:N0} folder pairs...",
                        CurrentProgress = processed,
                        MaxProgress = folderGroups.Count
                    });

                    await Task.Yield();
                }
            }

            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.AggregatingResults,
                StatusMessage = "Sorting folder matches by similarity...",
                IsIndeterminate = true
            });

            // Sort by similarity (this can also be expensive for large lists)
            var sortedMatches = folderMatches.OrderByDescending(f => f.SimilarityPercentage).ToList();

            return sortedMatches;
        }

        public List<FolderMatch> AggregateFolderMatches(List<FileMatch> fileMatches, ICacheManager cacheManager)
        {
            // Synchronous wrapper for backward compatibility
            return Task.Run(() => AggregateFolderMatchesAsync(fileMatches, cacheManager)).GetAwaiter().GetResult();
        }

        public List<FolderMatch> ApplyFilters(List<FolderMatch> matches, FilterCriteria criteria)
        {
            return matches.Where(match =>
                match.SimilarityPercentage >= criteria.MinimumSimilarityPercent &&
                match.FolderSizeBytes >= criteria.MinimumSizeBytes &&
                (criteria.MinimumDate == null || match.LatestModificationDate >= criteria.MinimumDate) &&
                (criteria.MaximumDate == null || match.LatestModificationDate <= criteria.MaximumDate)
            ).ToList();
        }

        public Task<string> ComputeFileHashAsync(string filePath) => ComputeFileHashAsync(filePath, CancellationToken.None);

        private async Task<string> ComputeFileHashAsync(string filePath, CancellationToken token, Action<int>? bytesRead = null, StorageWorkContext? work = null)
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    // Check if file exists and is accessible.
                    var before = new FileInfo(filePath);
                    var length = before.Length;
                    var modified = before.LastWriteTimeUtc;
                    await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 1024);
                    string hash;
                    try
                    {
                        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        int count;
                        while ((count = await stream.ReadAsync(buffer.AsMemory(0, 1024 * 1024), token)) > 0)
                        {
                            if (work == null) digest.AppendData(buffer, 0, count);
                            else await work.RunCpuAsync(() => digest.AppendData(buffer, 0, count), token).ConfigureAwait(false);
                            bytesRead?.Invoke(count);
                        }
                        hash = "";
                        if (work == null) hash = Convert.ToHexString(digest.GetHashAndReset());
                        else await work.RunCpuAsync(() => hash = Convert.ToHexString(digest.GetHashAndReset()), token).ConfigureAwait(false);
                    }
                    finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
                    before.Refresh();
                    if (before.Length != length || before.LastWriteTimeUtc != modified)
                        throw new IOException("File changed while being read.");
                    var metadata = _cacheManager.GetFileMetadata(filePath);
                    if (metadata != null && (metadata.Size != length || metadata.LastWriteTime.ToUniversalTime() != modified))
                        throw new IOException("File changed after scanning; run comparison again.");
                    return hash;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Use error recovery service for file access errors.
                    var action = await _errorRecoveryService.HandleFileAccessError(filePath, ex);
                    if (attempt == 0 && action.ShouldRetry)
                    {
                        await Task.Delay(action.RetryDelay, token);
                        continue;
                    }
                    _errorRecoveryService.LogSkippedItem(filePath, ex.Message);
                    return string.Empty;
                }
            }
        }

        private string CreateFileKey(string filePath)
        {
            try
            {
                var metadata = _cacheManager.GetFileMetadata(filePath);
                if (metadata != null)
                {
                    return $"{metadata.FileName.ToLowerInvariant()}_{metadata.Size}";
                }
                
                // Fallback to FileInfo if metadata not cached (shouldn't happen)
                var info = new FileInfo(filePath);
                return $"{info.Name.ToLowerInvariant()}_{info.Length}";
            }
            catch (Exception ex)
            {
                var recoveryAction = _errorRecoveryService.HandleFileAccessError(filePath, ex).Result;
                _errorRecoveryService.LogSkippedItem(filePath, $"Error creating file key: {ex.Message}");
                return filePath; // Fallback to full path if FileInfo fails
            }
        }
    }
}
