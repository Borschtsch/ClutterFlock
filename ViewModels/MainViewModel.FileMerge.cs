using System.IO;
using ClutterFlock.Core;
using ClutterFlock.Models;

namespace ClutterFlock.ViewModels;

public partial class MainViewModel
{
    private FileMergePlan? _preparedFileMerge;
    private FolderMatch? _fileMergePair;

    public async Task<FileMergePlan?> PrepareFileMergeAsync(FileDetailInfo row, bool toA)
    {
        _preparedFileMerge = null;
        if (!CanManageFolders || SelectedFolderMatch is not { } pair || !_allFileDetails.Contains(row)) return null;
        var source = toA ? row.RightFullPath : row.LeftFullPath;
        if (string.IsNullOrEmpty(source)) return null;
        var destination = toA ? row.LeftFullPath : row.RightFullPath;
        if (string.IsNullOrEmpty(destination)) destination = Path.Combine(toA ? pair.LeftFolder : pair.RightFolder, Path.GetFileName(source));
        if (!string.Equals(Path.GetDirectoryName(source), toA ? pair.RightFolder : pair.LeftFolder, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(destination), toA ? pair.LeftFolder : pair.RightFolder, StringComparison.OrdinalIgnoreCase)) return null;
        if (ProjectPath.Equals(source, StringComparison.OrdinalIgnoreCase) || ProjectPath.Equals(destination, StringComparison.OrdinalIgnoreCase))
        { StatusMessage = "Save the workspace elsewhere before changing its project file."; return null; }
        try
        {
            var token = BeginOperation("Checking the displayed file pair...");
            var plan = await _folderOperations.PrepareFileMergeAsync(source, destination, token);
            _preparedFileMerge = plan; _fileMergePair = pair;
            StatusMessage = "Ready to confirm: only the displayed file pair will change.";
            return plan;
        }
        catch (OperationCanceledException) { StatusMessage = "File check cancelled. No files changed."; return null; }
        catch (Exception ex) { StatusMessage = ex.Message; return null; }
        finally { EndOperation(); }
    }

    public async Task<bool> ExecuteFileMergeAsync(FileMergePlan plan)
    {
        if (!CanManageFolders || !ReferenceEquals(plan, _preparedFileMerge) || !ReferenceEquals(SelectedFolderMatch, _fileMergePair)) return false;
        _preparedFileMerge = null;
        var pair = SelectedFolderMatch!;
        var success = false;
        string message;
        try
        {
            var token = BeginOperation("Merging only the displayed file pair...");
            await _folderOperations.ExecuteFileMergeAsync(plan, token);
            success = true; message = "Displayed file pair merged. Other files and both folders were kept.";
        }
        catch (Exception ex) { message = $"File merge stopped: {ex.Message}"; }
        try
        {
            // Refresh both paths even after failure: a cross-device move can partially complete.
            var changed = new[] { plan.Source, plan.Destination };
            var metadata = await Task.Run(() => changed.Select(path =>
            {
                try
                {
                    var info = new FileInfo(path);
                    return info.Exists ? new FileMetadata { FileName = info.Name, Size = info.Length, LastWriteTime = info.LastWriteTime } : null;
                }
                catch { return null; }
            }).ToArray());
            for (var i = 0; i < changed.Length; i++)
            {
                var path = changed[i]; _cacheManager.RemoveFileFromCache(path);
                if (metadata[i] is not { } current) continue;
                _cacheManager.CacheFileMetadata(path, current);
                var parent = Path.GetDirectoryName(path)!;
                var files = _cacheManager.GetFolderFiles(parent).Append(path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _cacheManager.CacheFolderInfo(parent, new FolderInfo { Files = files,
                    TotalSize = files.Sum(p => _cacheManager.GetFileMetadata(p)?.Size ?? 0),
                    LatestModificationDate = files.Select(p => (DateTime?)_cacheManager.GetFileMetadata(p)?.LastWriteTime).Max() });
            }
            var remaining = _allFolderMatches.SelectMany(m => m.DuplicateFiles)
                .Where(m => !changed.Contains(m.PathA, StringComparer.OrdinalIgnoreCase) && !changed.Contains(m.PathB, StringComparer.OrdinalIgnoreCase)).ToList();
            _allFolderMatches = await _duplicateAnalyzer.AggregateFolderMatchesAsync(remaining, _cacheManager);
            foreach (var review in _workspace.Reviews.Where(r => r.LeftFolder == pair.LeftFolder || r.RightFolder == pair.RightFolder
                || r.LeftFolder == pair.RightFolder || r.RightFolder == pair.LeftFolder)) review.NeedsReview = true;
            _folderIndex = null; UpdateReviewSummaries(); await ApplyFiltersCoreAsync();
            await SelectFolderMatchAsync(FilteredFolderMatches.FirstOrDefault(m => m.LeftFolder == pair.LeftFolder && m.RightFolder == pair.RightFolder));
            _preparedFolderAction = null; MarkDirty(); NotifyWorkspace(); NotifySelection();
            StatusMessage = message + " Changed file evidence cleared; compare / refresh to verify current contents.";
            return success;
        }
        finally { EndOperation(); }
    }
}
