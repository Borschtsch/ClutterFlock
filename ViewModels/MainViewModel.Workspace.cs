using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using ClutterFlock.Core;
using ClutterFlock.Models;

namespace ClutterFlock.ViewModels;

public partial class MainViewModel
{
    private WorkspaceState _workspace = new();
    private List<string> _analysisRoots = new();
    private bool _restoringWorkspace;
    private bool _isDirty;
    private string _projectPath = "";
    private string _folderSearch = "", _focusFolder = "", _locationFilter = "";
    private string _relationshipFilter = "All relationships", _reviewFilter = "All reviews";
    private string _fileView = "All", _fileSearch = "";
    public ObservableCollection<LocationInfo> Locations { get; } = new();
    private List<FolderOverview>? _folderIndex;
    public IReadOnlyList<FolderOverview> FolderCatalog { get; private set; } = Array.Empty<FolderOverview>();
    public string[] RelationshipOptions { get; } = ["All relationships", "Identical files", "Containment", "Partial overlap"];
    public string[] ReviewOptions { get; } = ["Unreviewed", "Reviewed", "Investigate", "Ignore"];
    public string[] ReviewFilterOptions { get; } = ["All reviews", "Unreviewed", "Reviewed", "Investigate", "Ignore", "Bookmarked", "Needs review"];
    public string[] FileViews { get; } = ["All", "Differences", "Identical", "Only A", "Only B", "Different contents", "Unverified"];
    public string FolderSearch { get => _folderSearch; set => SetProperty(ref _folderSearch, value ?? ""); }
    public string FocusFolder { get => _focusFolder; set => SetProperty(ref _focusFolder, value ?? ""); }
    public string LocationFilter { get => _locationFilter; set => SetProperty(ref _locationFilter, value ?? ""); }
    public string RelationshipFilter { get => _relationshipFilter; set => SetProperty(ref _relationshipFilter, value ?? "All relationships"); }
    public string ReviewFilter { get => _reviewFilter; set => SetProperty(ref _reviewFilter, value ?? "All reviews"); }
    public string FileView
    {
        get => _fileView;
        set
        {
            if (!FileViews.Contains(value) || !SetProperty(ref _fileView, value)) return;
            _showUniqueFiles = value != "Identical";
            OnPropertyChanged(nameof(ShowUniqueFiles));
            FilterFileDetails();
        }
    }
    public string FileSearch
    {
        get => _fileSearch;
        set { if (SetProperty(ref _fileSearch, value ?? "")) FilterFileDetails(); }
    }
    public bool IsDirty { get => _isDirty; private set => SetProperty(ref _isDirty, value); }
    public string ProjectPath { get => _projectPath; private set => SetProperty(ref _projectPath, value); }
    public string ProjectTitle => (ProjectPath.Length == 0 ? "Untitled workspace" : Path.GetFileNameWithoutExtension(ProjectPath)) + (IsDirty ? " • Unsaved changes" : "");
    public string SaveSummary => _workspace.SavedAt is { } date ? $"Saved {date:g}" : "Not saved yet";
    public bool HasSelection => SelectedFolderMatch != null;
    public bool CanEditWorkspace => !OperationInProgress || IsAnalyzing;
    public bool RootsChanged => _hasAnalysis && !_analysisRoots.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(_scanFolders);
    public string SnapshotSummary => IsLivePreview ? "LIVE RESULTS · Verification in progress. Browse verified matches now; Cancel restores the previous analysis."
        : IsAnalyzing ? "Discovering locations · Previous analysis remains available while the next comparison is prepared."
        : !_hasAnalysis ? "Prepare locations, then compare. Existing projects can be explored offline."
        : $"Saved analysis evidence · {(_workspace.AnalyzedAt is { } date ? date.ToString("g") : "analysis date unknown")}"
          + (RootsChanged ? " · Locations changed — refresh needed" : " · Refresh to check current files");
    public string CoverageSummary => IsLivePreview ? "Partial analysis: more matches may appear. Relationships are provisional until verification finishes."
        : !_hasAnalysis ? ""
        : !_workspace.CoverageKnown ? "Analysis completeness was not recorded in this older project."
        : _workspace.AnalysisIssues.Count > 0 ? $"Incomplete analysis · {_workspace.AnalysisIssues.Count:N0} reported issues — view details"
        : "Analysis completed without reported access errors. Renamed copies are not matched.";
    public string IssuesText => _workspace.AnalysisIssues.Count == 0 ? CoverageSummary : string.Join(Environment.NewLine, _workspace.AnalysisIssues);
    public string OverviewSummary => $"{Locations.Count:N0} prepared locations · {_cacheManager.GetCachedFolderCount():N0} analyzed folders · {_allFolderMatches.Count:N0} folder pairs";
    public string ResultsSummary => $"{FilteredFolderMatches.Count:N0} of {_allFolderMatches.Count:N0} pairs";
    public string FocusSummary => FocusFolder.Length > 0 ? $"Matches involving: {FocusFolder}" : LocationFilter.Length > 0 ? $"Pairs involving: {LocationFilter}" : "All analyzed folder pairs";
    public string EmptyResultsMessage => IsLivePreview ? "Waiting for verified matches, or none pass the current filters. Content verification is still running." : OperationInProgress ? "Working… Previous results remain available until comparison completes."
        : !_hasAnalysis ? "Add locations or open a saved project, then compare to discover matching folders."
        : _allFolderMatches.Count == 0 ? "No same-name, identical-content matches found. Renamed files are outside the matching scope."
        : "No pairs match these filters. Reset filters or choose another folder.";
    public string ComparisonSummary => SelectedFolderMatch == null ? "Select a folder pair to inspect its evidence."
        : string.Join(" · ", new[] { "Identical", "Only A", "Only B", "Different contents", "Unverified" }
            .Select(status => $"{_allFileDetails.Count(f => f.Status == status):N0} {status.ToLowerInvariant()}"));
    public string ReviewStatus
    {
        get => FindReview()?.Status ?? "Unreviewed";
        set { if (CanReview && ReviewOptions.Contains(value)) { EnsureReview().Status = value; EnsureReview().NeedsReview = false; ReviewChanged(); } }
    }
    public string ReviewNotes
    {
        get => FindReview()?.Notes ?? "";
        set { if (CanReview && ReviewNotes != value) { EnsureReview().Notes = value ?? ""; MarkDirty(); } }
    }
    public bool IsBookmarked
    {
        get => FindReview()?.Bookmarked ?? false;
        set { if (CanReview) { EnsureReview().Bookmarked = value; ReviewChanged(); } }
    }
    public string ReviewNotice => FindReview()?.NeedsReview == true ? "Analysis refreshed since this review. Check the evidence and set a review status again." : "Review and notes are saved with the project.";
    public double LocationsWidth { get => _workspace.LocationsWidth; set { _workspace.LocationsWidth = Math.Clamp(value, 180, 500); MarkDirty(); } }
    public double ComparisonsWidth { get => _workspace.ComparisonsWidth; set { _workspace.ComparisonsWidth = Math.Clamp(value, 280, 900); MarkDirty(); } }

    private static string DefaultLabel(string path) => Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path;
    private void MarkDirty() { if (!_restoringWorkspace) IsDirty = true; OnPropertyChanged(nameof(ProjectTitle)); }
    private void TrackWorkspaceChange(object? sender, PropertyChangedEventArgs e)
    {
        if (IsAnalyzing && e.PropertyName is nameof(FolderSearch) or nameof(FocusFolder) or nameof(LocationFilter)
            or nameof(RelationshipFilter) or nameof(ReviewFilter) or nameof(FileView) or nameof(FileSearch)
            or nameof(MinimumSimilarity) or nameof(MinimumSizeMB) or nameof(ShowUniqueFiles)) _analysisWorkspaceChanged = true;
        if (e.PropertyName is nameof(FolderSearch) or nameof(FocusFolder) or nameof(LocationFilter) or nameof(RelationshipFilter)
            or nameof(ReviewFilter) or nameof(FileView) or nameof(FileSearch) or nameof(MinimumSimilarity) or nameof(MinimumSizeMB)
            or nameof(ShowUniqueFiles) or nameof(SelectedFolderMatch)) MarkDirty();
        if (e.PropertyName == nameof(OperationInProgress)) { OnPropertyChanged(nameof(EmptyResultsMessage)); OnPropertyChanged(nameof(CanEditWorkspace)); }
    }
    public void RenameLocation(string path, string label)
    {
        if (!CanAddFolders) return;
        var index = Locations.ToList().FindIndex(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        Locations[index] = new LocationInfo { Path = Locations[index].Path, Label = string.IsNullOrWhiteSpace(label) ? DefaultLabel(path) : label.Trim() };
        UpdateReviewSummaries();
        if (_folderIndex != null) _folderIndex = _folderIndex.Select(f => f with { Context = FolderContext(f.Path) }).ToList();
        FolderCatalog = FolderCatalog.Select(f => f with { Context = FolderContext(f.Path) }).ToList();
        OnPropertyChanged(nameof(FolderCatalog)); MarkDirty();
    }
    public bool IsUpdatingFilterValues { get; private set; }
    public Task ResetFiltersAsync() => ShowFolderComparisonsAsync("");

    public async Task ShowFolderComparisonsAsync(string folderPath)
    {
        // Bound controls can raise filter events synchronously as these values change.
        // Set the complete destination before allowing any filter evaluation.
        IsUpdatingFilterValues = true;
        try
        {
            FolderSearch = LocationFilter = "";
            FocusFolder = folderPath;
            RelationshipFilter = "All relationships";
            ReviewFilter = "All reviews";
            MinimumSimilarity = MinimumSizeMB = 0;
        }
        finally { IsUpdatingFilterValues = false; }
        await ApplyFiltersAsync();
    }
    private static bool MatchesWorkspace(FolderMatch m, string search, string focus, string location, string relationship, string review)
    {
        return (search.Length == 0 || m.LeftFolder.Contains(search, StringComparison.OrdinalIgnoreCase) || m.RightFolder.Contains(search, StringComparison.OrdinalIgnoreCase))
            && (focus.Length == 0 || m.LeftFolder.Equals(focus, StringComparison.OrdinalIgnoreCase) || m.RightFolder.Equals(focus, StringComparison.OrdinalIgnoreCase))
            && (location.Length == 0 || PathUtilities.IsWithin(m.LeftFolder, location) || PathUtilities.IsWithin(m.RightFolder, location))
            && (relationship == "All relationships" || relationship == "Containment" && m.Relationship.Contains("contains") || m.Relationship == relationship)
            && (review == "All reviews" || m.ReviewSummary.Contains(review, StringComparison.Ordinal));
    }
    private async Task RebuildFolderCatalogAsync(int version)
    {
        // Reuse the folder index across pair filters and replace the bound list once.
        // Building and searching a large archive must not insert thousands of UI rows individually.
        var existing = _folderIndex;
        var cache = _cacheManager;
        var matches = existing == null ? _allFolderMatches.ToList() : null;
        var provisional = IsLivePreview;
        var locations = Locations.Concat(_workspace.Locations).ToList();
        var search = FolderSearch.Trim();
        var location = LocationFilter;
        var result = await Task.Run(() =>
        {
            var index = existing;
            if (index == null)
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in matches!)
                {
                    counts[m.LeftFolder] = counts.GetValueOrDefault(m.LeftFolder) + 1;
                    counts[m.RightFolder] = counts.GetValueOrDefault(m.RightFolder) + 1;
                }
                index = cache.GetAllFolderInfo().OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new FolderOverview(f.Key, f.Value.FileCount, counts.GetValueOrDefault(f.Key))
                        { Context = FolderContext(f.Key, locations), IsProvisional = provisional }).ToList();
            }
            var visible = index.Where(f => (location.Length == 0 || PathUtilities.IsWithin(f.Path, location))
                && f.Path.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            return (index, visible);
        });
        if (_disposed || version != _filterVersion) return;
        _folderIndex = result.index;
        FolderCatalog = result.visible;
        OnPropertyChanged(nameof(FolderCatalog));
    }
    private string FolderContext(string path) => FolderContext(path, Locations.Concat(_workspace.Locations));
    private static string FolderContext(string path, IEnumerable<LocationInfo> locations)
    {
        var root = locations.Where(l => PathUtilities.IsWithin(path, l.Path)).OrderByDescending(l => l.Path.Length).FirstOrDefault();
        if (root == null) return path;
        var relative = Path.GetRelativePath(root.Path, path);
        return relative == "." ? root.DisplayName : $"{root.DisplayName} › {relative}";
    }
    public string EmptyFilesMessage => HasSelection ? "No file rows match this view. Choose All or clear the file search." : "Select a folder pair to inspect its files.";
    private PairReview? FindReview() => SelectedFolderMatch is { } m ? _workspace.Reviews.FirstOrDefault(r => SamePair(r, m)) : null;
    private static bool SamePair(PairReview r, FolderMatch m) => r.LeftFolder.Equals(m.LeftFolder, StringComparison.OrdinalIgnoreCase) && r.RightFolder.Equals(m.RightFolder, StringComparison.OrdinalIgnoreCase);
    private PairReview EnsureReview()
    {
        var review = FindReview();
        if (review != null) return review;
        review = new PairReview { LeftFolder = SelectedFolderMatch!.LeftFolder, RightFolder = SelectedFolderMatch.RightFolder };
        _workspace.Reviews.Add(review);
        return review;
    }
    private void ReviewChanged()
    {
        if (SelectedFolderMatch is { } match) match.ReviewSummary = FormatReview(FindReview());
        MarkDirty(); NotifySelection();
    }
    private static string FormatReview(PairReview? review) => (review?.Status ?? "Unreviewed")
        + (review?.Bookmarked == true ? " · Bookmarked" : "") + (review?.NeedsReview == true ? " · Needs review" : "");
    private void UpdateReviewSummaries()
    {
        var reviews = new Dictionary<string, Dictionary<string, PairReview>>(StringComparer.OrdinalIgnoreCase);
        foreach (var review in _workspace.Reviews)
        {
            if (!reviews.TryGetValue(review.LeftFolder, out var right)) reviews[review.LeftFolder] = right = new(StringComparer.OrdinalIgnoreCase);
            right[review.RightFolder] = review;
        }
        var contexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Context(string path)
        {
            if (!contexts.TryGetValue(path, out var value)) contexts[path] = value = FolderContext(path);
            return value;
        }
        foreach (var m in _allFolderMatches)
        {
            m.LeftContext = Context(m.LeftFolder); m.RightContext = Context(m.RightFolder);
            m.ReviewSummary = FormatReview(reviews.GetValueOrDefault(m.LeftFolder)?.GetValueOrDefault(m.RightFolder));
        }
    }
    private WorkspaceState CaptureWorkspace(DateTime savedAt) => new()
    {
        Locations = Locations.Select(l => new LocationInfo { Path = l.Path, Label = l.Label }).ToList(),
        Reviews = _workspace.Reviews.Select(r => new PairReview { LeftFolder = r.LeftFolder, RightFolder = r.RightFolder,
            Notes = r.Notes, Status = r.Status, Bookmarked = r.Bookmarked, NeedsReview = r.NeedsReview }).ToList(), AnalyzedAt = _workspace.AnalyzedAt, SavedAt = savedAt,
        CoverageKnown = _workspace.CoverageKnown, AnalysisIssues = _workspace.AnalysisIssues.ToList(),
        FolderSearch = FolderSearch, FocusFolder = FocusFolder, LocationFilter = LocationFilter,
        RelationshipFilter = RelationshipFilter, ReviewFilter = ReviewFilter, FileView = FileView, FileSearch = FileSearch,
        ReadConcurrency = ReadConcurrency, LocationsWidth = LocationsWidth, ComparisonsWidth = ComparisonsWidth
    };
    private void RestoreWorkspaceFilters(WorkspaceState? saved)
    {
        _readConcurrency = ReadConcurrencyOptions.Contains(saved?.ReadConcurrency ?? 8) ? saved?.ReadConcurrency ?? 8 : 8;
        OnPropertyChanged(nameof(ReadConcurrency));
        FolderSearch = saved?.FolderSearch ?? ""; FocusFolder = saved?.FocusFolder ?? "";
        LocationFilter = saved?.LocationFilter ?? "";
        RelationshipFilter = RelationshipOptions.Contains(saved?.RelationshipFilter) ? saved!.RelationshipFilter : "All relationships";
        ReviewFilter = ReviewFilterOptions.Contains(saved?.ReviewFilter) ? saved!.ReviewFilter : "All reviews";
        FileView = FileViews.Contains(saved?.FileView) ? saved!.FileView : ShowUniqueFiles ? "All" : "Identical";
        FileSearch = saved?.FileSearch ?? "";
    }
    private void NotifySelection()
    {
        foreach (var name in new[] { nameof(HasSelection), nameof(CanReview), nameof(CanManageFolders), nameof(EmptyFilesMessage), nameof(ComparisonSummary), nameof(ReviewStatus), nameof(ReviewNotes), nameof(IsBookmarked), nameof(ReviewNotice) }) OnPropertyChanged(name);
    }
    private void NotifyWorkspace()
    {
        foreach (var name in new[] { nameof(ProjectTitle), nameof(SaveSummary), nameof(SnapshotSummary), nameof(CoverageSummary), nameof(IssuesText),
            nameof(OverviewSummary), nameof(ResultsSummary), nameof(FocusSummary), nameof(EmptyResultsMessage), nameof(RootsChanged), nameof(LocationsWidth), nameof(ComparisonsWidth) }) OnPropertyChanged(name);
    }
    public void NewProject()
    {
        if (!CanLoadProject) return;
        _restoringWorkspace = true;
        ClearAnalysis(); SetCache(new CacheManager()); _analysisRoots.Clear(); _scanFolders.Clear(); ScanFolders.Clear(); Locations.Clear(); FolderCatalog = Array.Empty<FolderOverview>(); OnPropertyChanged(nameof(FolderCatalog));
        _workspace = new(); _createdDate = DateTime.Now; ProjectPath = ""; MinimumSimilarity = MinimumSizeMB = 0;
        RestoreWorkspaceFilters(_workspace); IsDirty = false; _restoringWorkspace = false;
        StatusMessage = "Add locations or open a saved project."; NotifyWorkspace(); NotifyAvailability(); NotifySelection();
    }
}
