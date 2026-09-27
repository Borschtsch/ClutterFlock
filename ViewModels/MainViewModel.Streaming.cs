using System.Diagnostics;
using System.IO;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.ViewModels;

public partial class MainViewModel
{
    private bool _analysisWorkspaceChanged;
    private int _readConcurrency = 8;
    public int[] ReadConcurrencyOptions { get; } = [1, 2, 4, 8, 16, 32];
    public int ReadConcurrency
    {
        get => _readConcurrency;
        set { if (CanAddFolders && ReadConcurrencyOptions.Contains(value) && SetProperty(ref _readConcurrency, value)) MarkDirty(); }
    }
    public AnalysisStatistics? LastAnalysisStatistics { get; private set; }
    public bool IsAnalyzing { get; private set; }
    public bool IsLivePreview { get; private set; }
    public bool CanReview => HasSelection && !OperationInProgress;
    private readonly HashSet<FolderMatch> _visibleLiveMatches = new();
    private void SetAnalysisState(bool analyzing, bool preview)
    {
        if (analyzing && !IsAnalyzing) _analysisWorkspaceChanged = false;
        IsAnalyzing = analyzing; IsLivePreview = preview;
        if (!preview) _visibleLiveMatches.Clear();
        foreach (var property in new[] { nameof(IsAnalyzing), nameof(IsLivePreview), nameof(CanEditWorkspace), nameof(CanReview) }) OnPropertyChanged(property);
        NotifyWorkspace(); NotifyAvailability();
    }
    private async Task StreamVerifiedMatchesAsync(ICacheManager cache, IProgress<AnalysisProgress> progress, CancellationToken token, StorageTopology topology)
    {
        var buffer = new AnalysisResultBuffer();
        var stats = new AnalysisStatistics();
        LastAnalysisStatistics = stats;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var options = new AnalysisOptions { StorageTopology = topology, QualifyingFoldersOnly = true, RetainFileMatches = false, Statistics = stats };
        var producing = Task.Run(() => new DuplicateAnalyzer(cache, _errorRecoveryService).FindDuplicateFilesAsync(
            cache.GetAllFolderInfo().Keys.ToList(), progress, stop.Token,
            (batch, cancellation) => { cancellation.ThrowIfCancellationRequested(); buffer.Add(batch); return Task.CompletedTask; }, options));
        try { await ConsumeVerifiedMatchesAsync(buffer, producing, cache, stop.Token); await producing; }
        catch
        {
            stop.Cancel();
            try { await producing; } catch { /* Observe the producer before discarding the preview. */ }
            throw;
        }
    }
    private async Task ConsumeVerifiedMatchesAsync(AnalysisResultBuffer buffer, Task producing, ICacheManager cache, CancellationToken token)
    {
        var pairs = new Dictionary<(string, string), FolderMatch>();
        var contexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Context(string folder)
        {
            if (!contexts.TryGetValue(folder, out var context)) contexts[folder] = context = FolderContext(folder);
            return context;
        }
        _visibleLiveMatches.Clear();
        var detailClock = Stopwatch.StartNew();
        var refreshDetails = false;
        while (!producing.IsCompleted || buffer.HasChanges)
        {
            token.ThrowIfCancellationRequested();
            var updates = buffer.Take();
            foreach (var update in updates)
            {
                token.ThrowIfCancellationRequested();
                var left = update.LeftFolder; var right = update.RightFolder;
                if (!pairs.TryGetValue((left, right), out var match))
                {
                    var a = cache.GetFolderInfo(left)!; var b = cache.GetFolderInfo(right)!;
                    match = new FolderMatch(left, right, new(), a.FileCount, b.FileCount, Math.Max(a.TotalSize, b.TotalSize))
                    {
                        IsProvisional = true, LeftContext = Context(left), RightContext = Context(right),
                        LatestModificationDate = new[] { a.LatestModificationDate, b.LatestModificationDate }.Max()
                    };
                    pairs[(left, right)] = match;
                    _allFolderMatches.Add(match);
                }
                match.DuplicateFiles.AddRange(update.Files);
                match.RefreshEvidence();
                refreshDetails |= ReferenceEquals(match, SelectedFolderMatch);
                if (!IsPopulatingResults && PassesLiveFilters(match) && _visibleLiveMatches.Add(match)) FilteredFolderMatches.Add(match);
            }
            if (updates.Count > 0)
            {
                OnPropertyChanged(nameof(ResultsSummary)); OnPropertyChanged(nameof(OverviewSummary));
                if (producing.IsCompleted) StatusMessage = $"Verification finished · displaying {_allFolderMatches.Count:N0} folder pairs…";
            }
            if (refreshDetails && detailClock.ElapsedMilliseconds >= 500 && !IsPopulatingResults)
            {
                await UpdateFileDetailsAsync(); refreshDetails = false; detailClock.Restart();
            }
            // UI cadence is independent of producer speed; completion drains without the preview delay.
            await Task.Delay(producing.IsCompleted ? 1 : 100, token);
        }
    }
    private bool PassesLiveFilters(FolderMatch match) => match.SimilarityPercentage >= MinimumSimilarity
        && match.FolderSizeBytes / (1024.0 * 1024) >= MinimumSizeMB
        && MatchesWorkspace(match, FolderSearch.Trim(), FocusFolder, LocationFilter, RelationshipFilter, ReviewFilter);
}

// Historical streaming notes: batching now happens in the background; UI backpressure is removed.
        // Bounded batches apply backpressure: a fast disk cannot flood the dispatcher.
                // Keep empty-file evidence pending until this pair shares actual content.
            // Yield after each bounded batch so selection, filtering and cancel keep working.
