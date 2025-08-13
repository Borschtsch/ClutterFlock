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
    public class DuplicateAnalyzer : IDuplicateAnalyzer
    {
        private readonly ICacheManager _cacheManager;
        private readonly IErrorRecoveryService _errorRecoveryService;
        private static readonly int MaxParallelism = Math.Max(1, Environment.ProcessorCount - 1);

        public DuplicateAnalyzer(ICacheManager cacheManager, IErrorRecoveryService errorRecoveryService)
        {
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _errorRecoveryService = errorRecoveryService ?? throw new ArgumentNullException(nameof(errorRecoveryService));
        }

        public async Task<List<FileMatch>> FindDuplicateFilesAsync(List<string> folders, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
        {
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

            var duplicateMatches = await CompareFileHashesAsync(potentialDuplicateGroups, progress, cancellationToken);

            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.Complete,
                StatusMessage = $"Found {duplicateMatches.Count:N0} duplicate files",
                CurrentProgress = duplicateMatches.Count,
                MaxProgress = duplicateMatches.Count
            });

            return duplicateMatches;
        }

        private async Task<Dictionary<(string, long), List<string>>> BuildFileIndexAsync(List<string> folders, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
        {
            var fileIndex = new Dictionary<(string, long), List<string>>();
            int processedFolders = 0;

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
                        
                        var key = (metadata.FileName.ToLowerInvariant(), metadata.Size);
                        
                        if (!fileIndex.TryGetValue(key, out var list))
                            fileIndex[key] = list = new List<string>();
                        
                        if (!list.Contains(folder))
                            list.Add(folder);
                    }
                    catch (Exception ex)
                    {
                        // Use error recovery service for file access errors
                        var recoveryAction = await _errorRecoveryService.HandleFileAccessError(file, ex);
                        _errorRecoveryService.LogSkippedItem(file, $"File metadata access failed: {ex.Message}");
                        continue;
                    }

                    // Yield control every 100 files to keep UI responsive
                    processedFiles++;
                    if (processedFiles % 100 == 0)
                    {
                        await Task.Yield();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                var current = ++processedFolders;
                if (current % 10 == 0 || current == folders.Count)
                {
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

        private async Task<Dictionary<string, List<string>>> GroupPotentialDuplicatesAsync(Dictionary<(string, long), List<string>> fileIndex, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
        {
            var fileGroups = new Dictionary<string, List<string>>();
            var processedGroups = 0;
            var totalGroups = fileIndex.Where(x => x.Value.Count > 1).Count();
            var processedFiles = 0;
            
            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.BuildingFileIndex,
                StatusMessage = $"Starting to group {totalGroups} potential duplicate groups...",
                CurrentProgress = 0,
                MaxProgress = totalGroups
            });
            
            // Process each filename/size group that has multiple folders
            foreach (var kvp in fileIndex.Where(x => x.Value.Count > 1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var (fileName, fileSize) = kvp.Key;
                var foldersWithThisFile = kvp.Value;
                
                // Get all actual file paths for this filename/size combination
                foreach (var folder in foldersWithThisFile)
                {
                    var folderFiles = _cacheManager.GetFolderFiles(folder);
                    foreach (var file in folderFiles)
                    {
                        var metadata = _cacheManager.GetFileMetadata(file);
                        if (metadata != null && 
                            metadata.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) && 
                            metadata.Size == fileSize)
                        {
                            var key = CreateFileKey(file);
                            if (!fileGroups.TryGetValue(key, out var list))
                                fileGroups[key] = list = new List<string>();
                            list.Add(file);
                        }
                        
                        // Update progress more frequently - every 50 files OR every 10th group
                        processedFiles++;
                        if (processedFiles % 50 == 0)
                        {
                            progress?.Report(new AnalysisProgress
                            {
                                Phase = AnalysisPhase.BuildingFileIndex,
                                StatusMessage = $"Grouping duplicates: {processedGroups + 1}/{totalGroups} groups ({processedFiles} files processed)...",
                                CurrentProgress = processedGroups,
                                MaxProgress = totalGroups
                            });
                            
                            // Allow UI updates more frequently
                            await Task.Yield();
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                }

                var current = ++processedGroups;
                
                // Always report progress after each group
                progress?.Report(new AnalysisProgress
                {
                    Phase = AnalysisPhase.BuildingFileIndex,
                    StatusMessage = $"Grouping potential duplicates: {current}/{totalGroups} groups...",
                    CurrentProgress = current,
                    MaxProgress = totalGroups
                });

                // Always yield after each group to keep UI responsive
                await Task.Yield();
            }

            // Return only groups with multiple files (potential duplicates)
            var result = fileGroups.Where(g => g.Value.Count > 1).ToDictionary(g => g.Key, g => g.Value);
            
            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.BuildingFileIndex,
                StatusMessage = $"Found {result.Count} potential duplicate groups",
                CurrentProgress = totalGroups,
                MaxProgress = totalGroups
            });

            return result;
        }

        private async Task<List<FileMatch>> CompareFileHashesAsync(Dictionary<string, List<string>> duplicateGroups, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken)
        {
            var matches = new ConcurrentBag<FileMatch>();
            int processedGroups = 0;

            await Task.Run(() =>
            {
                Parallel.ForEach(duplicateGroups.Values, new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelism,
                    CancellationToken = cancellationToken
                }, fileGroup =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    for (int i = 0; i < fileGroup.Count; i++)
                    {
                        var folderA = Path.GetDirectoryName(fileGroup[i]) ?? string.Empty;
                        var hashA = GetOrComputeFileHash(fileGroup[i]);
                        if (string.IsNullOrEmpty(hashA)) continue;

                        for (int j = i + 1; j < fileGroup.Count; j++)
                        {
                            var folderB = Path.GetDirectoryName(fileGroup[j]) ?? string.Empty;
                            if (folderA == folderB) continue; // Skip files in same folder

                            var hashB = GetOrComputeFileHash(fileGroup[j]);
                            if (hashA == hashB)
                            {
                                matches.Add(new FileMatch(fileGroup[i], fileGroup[j]));
                            }
                        }
                    }

                    var current = Interlocked.Increment(ref processedGroups);
                    if (current % 10 == 0 || current == duplicateGroups.Count)
                    {
                        progress?.Report(new AnalysisProgress
                        {
                            Phase = AnalysisPhase.ComparingFiles,
                            StatusMessage = $"Processed {current:N0} of {duplicateGroups.Count:N0} groups... ({matches.Count:N0} matches found)",
                            CurrentProgress = current,
                            MaxProgress = duplicateGroups.Count
                        });
                    }
                });
            }, cancellationToken);

            return matches.ToList();
        }

        public async Task<List<FolderMatch>> AggregateFolderMatchesAsync(List<FileMatch> fileMatches, ICacheManager cacheManager, IProgress<AnalysisProgress>? progress = null)
        {
            if (fileMatches.Count == 0) return new List<FolderMatch>();

            progress?.Report(new AnalysisProgress
            {
                Phase = AnalysisPhase.AggregatingResults,
                StatusMessage = "Grouping file matches by folders...",
                IsIndeterminate = true
            });

            var folderGroups = fileMatches
                .GroupBy(m => (Path.GetDirectoryName(m.PathA) ?? string.Empty, Path.GetDirectoryName(m.PathB) ?? string.Empty))
                .Where(g => !string.IsNullOrEmpty(g.Key.Item1) && !string.IsNullOrEmpty(g.Key.Item2))
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
                var leftFolder = group.Key.Item1;
                var rightFolder = group.Key.Item2;
                var duplicateFiles = group.ToList();

                var leftInfo = cacheManager.GetFolderInfo(leftFolder);
                var rightInfo = cacheManager.GetFolderInfo(rightFolder);

                var totalLeftFiles = leftInfo?.FileCount ?? 0;
                var totalRightFiles = rightInfo?.FileCount ?? 0;
                var folderSize = leftInfo?.TotalSize ?? 0;

                var folderMatch = new FolderMatch(leftFolder, rightFolder, duplicateFiles, 
                    totalLeftFiles, totalRightFiles, folderSize)
                {
                    LatestModificationDate = leftInfo?.LatestModificationDate
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
            return AggregateFolderMatchesAsync(fileMatches, cacheManager).GetAwaiter().GetResult();
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

        public async Task<string> ComputeFileHashAsync(string filePath)
        {
            return await Task.Run(() => ComputeFileHash(filePath));
        }

        private string GetOrComputeFileHash(string filePath)
        {
            var cachedHash = _cacheManager.GetFileHash(filePath);
            if (cachedHash != null) return cachedHash;

            var hash = ComputeFileHash(filePath);
            if (!string.IsNullOrEmpty(hash))
                _cacheManager.CacheFileHash(filePath, hash);

            return hash;
        }

        private string ComputeFileHash(string filePath)
        {
            try
            {
                // Check if file exists and is accessible
                if (!File.Exists(filePath))
                    return string.Empty;

                using var sha = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
            catch (UnauthorizedAccessException ex)
            {
                var recoveryAction = _errorRecoveryService.HandleFileAccessError(filePath, ex).Result;
                _errorRecoveryService.LogSkippedItem(filePath, "File access denied for hash computation");
                return string.Empty;
            }
            catch (IOException ex)
            {
                var recoveryAction = _errorRecoveryService.HandleFileAccessError(filePath, ex).Result;
                _errorRecoveryService.LogSkippedItem(filePath, $"IO error during hash computation: {ex.Message}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                var recoveryAction = _errorRecoveryService.HandleFileAccessError(filePath, ex).Result;
                _errorRecoveryService.LogSkippedItem(filePath, $"Unexpected error during hash computation: {ex.Message}");
                return string.Empty;
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
