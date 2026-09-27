using System.IO;
using ClutterFlock.Core;
using ClutterFlock.Models;

namespace ClutterFlock.ViewModels;

public partial class MainViewModel
{
    private readonly FolderOperations _folderOperations = new();
    private FolderOperationPlan? _preparedFolderAction;
    public bool CanManageFolders => CanLoadProject && SelectedFolderMatch is { IsProvisional: false };

    public async Task<FolderOperationPlan?> PrepareFolderActionAsync(FolderAction action)
    {
        if (!CanManageFolders) return null;
        var pair = SelectedFolderMatch!;
        _preparedFolderAction = null;
        try
        {
            var token = BeginOperation("Checking current folders, subfolders and conflicts...");
            var plan = await _folderOperations.PrepareAsync(action, pair.LeftFolder, pair.RightFolder, token);
            if (ProjectPath.Length > 0 && (PathUtilities.IsWithin(ProjectPath, plan.Source)
                || plan.Destination != null && PathUtilities.IsWithin(ProjectPath, plan.Destination)))
                throw new IOException("Save the workspace outside these folders before changing them.");
            _preparedFolderAction = plan;
            StatusMessage = plan.CanExecute ? "Ready for confirmation. No files changed."
                : plan.Subfolders.Count > 0 ? "Action blocked: inspect and resolve subfolders manually first."
                : $"Merge blocked: resolve {plan.Conflicts.Count:N0} file conflicts first.";
            return plan;
        }
        catch (OperationCanceledException) { StatusMessage = "Folder check cancelled. No files changed."; return null; }
        catch (Exception ex) { StatusMessage = $"Cannot prepare action: {ex.Message}"; return null; }
        finally { EndOperation(); }
    }

    public async Task<bool> ExecuteFolderActionAsync(FolderOperationPlan plan)
    {
        if (!CanManageFolders || !ReferenceEquals(plan, _preparedFolderAction) || !plan.CanExecute) return false;
        _preparedFolderAction = null;
        string result;
        var succeeded = false;
        try
        {
            var token = BeginOperation($"{plan.Title} in progress...");
            var version = _operationVersion;
            var progress = new Progress<string>(message => { if (OperationInProgress && version == _operationVersion) StatusMessage = message; });
            await _folderOperations.ExecuteAsync(plan, progress, token);
            succeeded = true; result = $"{plan.Title} completed.";
        }
        catch (OperationCanceledException) { result = "Action stopped. Completed changes were retained."; }
        catch (Exception ex) { result = $"Action stopped: {ex.Message} Completed changes, if any, were retained."; }
        try
        {
            // A partial operation is also a change: never restore evidence for changed paths.
            var affected = new[] { plan.Source, plan.Destination }.OfType<string>().ToArray();
            foreach (var path in affected) _cacheManager.RemoveFolderFromCache(path);
            _allFolderMatches.RemoveAll(m => affected.Any(p => PathUtilities.IsWithin(m.LeftFolder, p) || PathUtilities.IsWithin(m.RightFolder, p)));
            if (succeeded && !Directory.Exists(plan.Source))
            {
                _scanFolders.RemoveAll(p => p.Equals(plan.Source, StringComparison.OrdinalIgnoreCase));
                foreach (var root in ScanFolders.Where(p => p.Equals(plan.Source, StringComparison.OrdinalIgnoreCase)).ToList()) ScanFolders.Remove(root);
                foreach (var location in Locations.Where(l => l.Path.Equals(plan.Source, StringComparison.OrdinalIgnoreCase)).ToList()) Locations.Remove(location);
            }
            _folderIndex = null;
            _workspace.AnalysisIssues.Add($"{plan.Title}: affected folder evidence removed. Compare / refresh to verify current contents.");
            foreach (var review in _workspace.Reviews.Where(r => affected.Any(p => PathUtilities.IsWithin(r.LeftFolder, p) || PathUtilities.IsWithin(r.RightFolder, p)))) review.NeedsReview = true;
            SelectedFolderMatch = null;
            await ApplyFiltersCoreAsync(); MarkDirty(); NotifyWorkspace(); NotifySelection();
            StatusMessage = result + " Affected evidence removed; compare / refresh to verify current files.";
            return succeeded;
        }
        finally { EndOperation(); }
    }

    public async Task<bool> DeleteFileAsync(string path)
    {
        if (!CanManageFolders || SelectedFolderMatch is not { } selected) return false;
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.Equals(parent, selected.LeftFolder, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parent, selected.RightFolder, StringComparison.OrdinalIgnoreCase)) return false;
        if (ProjectPath.Equals(path, StringComparison.OrdinalIgnoreCase)) { StatusMessage = "Save the workspace elsewhere before deleting its project file."; return false; }
        try
        {
            BeginOperation("Deleting the confirmed file...", cancellable: false);
            await _folderOperations.DeleteFileAsync(path);
            _cacheManager.RemoveFileFromCache(path);
            var remaining = _allFolderMatches.SelectMany(m => m.DuplicateFiles)
                .Where(m => !m.PathA.Equals(path, StringComparison.OrdinalIgnoreCase) && !m.PathB.Equals(path, StringComparison.OrdinalIgnoreCase)).ToList();
            _allFolderMatches = await _duplicateAnalyzer.AggregateFolderMatchesAsync(remaining, _cacheManager);
            foreach (var review in _workspace.Reviews.Where(r => r.LeftFolder.Equals(parent, StringComparison.OrdinalIgnoreCase)
                || r.RightFolder.Equals(parent, StringComparison.OrdinalIgnoreCase))) review.NeedsReview = true;
            _folderIndex = null; UpdateReviewSummaries();
            await ApplyFiltersCoreAsync();
            await SelectFolderMatchAsync(FilteredFolderMatches.FirstOrDefault(m => m.LeftFolder == selected.LeftFolder && m.RightFolder == selected.RightFolder));
            _preparedFolderAction = null; MarkDirty(); NotifyWorkspace(); NotifySelection();
            StatusMessage = $"Permanently deleted: {path}. File evidence updated; merge requires a new check.";
            return true;
        }
        catch (Exception ex) { StatusMessage = $"File deletion failed: {ex.Message}"; return false; }
        finally { EndOperation(); }
    }
}
