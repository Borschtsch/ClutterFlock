using System.IO;

namespace ClutterFlock.Models;

// Optional workspace data keeps older snapshots readable without changing the file index.
public sealed class WorkspaceState
{
    public List<LocationInfo> Locations { get; set; } = new();
    public List<PairReview> Reviews { get; set; } = new();
    public DateTime? AnalyzedAt { get; set; }
    public DateTime? SavedAt { get; set; }
    public List<string> AnalysisIssues { get; set; } = new();
    public bool CoverageKnown { get; set; }
    public string FolderSearch { get; set; } = "";
    public string FocusFolder { get; set; } = "";
    public string LocationFilter { get; set; } = "";
    public string RelationshipFilter { get; set; } = "All relationships";
    public string ReviewFilter { get; set; } = "All reviews";
    public string FileView { get; set; } = "All";
    public string FileSearch { get; set; } = "";
    public int ReadConcurrency { get; set; } = 8;
    public double LocationsWidth { get; set; } = 230;
    public double ComparisonsWidth { get; set; } = 640;
}

public sealed class LocationInfo
{
    public string Path { get; set; } = "";
    public string Label { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Path : Label;
}

public sealed class PairReview
{
    public string LeftFolder { get; set; } = "";
    public string RightFolder { get; set; } = "";
    public string Status { get; set; } = "Unreviewed";
    public string Notes { get; set; } = "";
    public bool Bookmarked { get; set; }
    public bool NeedsReview { get; set; }
}

public sealed record FolderOverview(string Path, int FileCount, int MatchCount)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : Path;
    public string Context { get; init; } = "";
    public bool IsProvisional { get; init; }
    public string Summary => IsProvisional ? $"{FileCount:N0} direct files · analysis running" : $"{FileCount:N0} direct files · {MatchCount:N0} matching locations";
}
