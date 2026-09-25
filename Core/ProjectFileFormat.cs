using System.Text.Json.Serialization;
using ClutterFlock.Models;

namespace ClutterFlock.Core
{
    // These types describe disk storage only. The application's service interfaces
    // continue to use ProjectData and the existing full-path caches in memory.
    internal sealed class StoredProject
    {
        [JsonRequired] public string ApplicationName { get; set; } = "ClutterFlock";
        [JsonRequired] public string Version { get; set; } = "3.0";
        [JsonRequired] public List<string> ScanFolders { get; set; } = new();
        [JsonRequired] public DateTime CreatedDate { get; set; }
        [JsonRequired] public bool HasAnalysis { get; set; }
        [JsonRequired] public FilterCriteria Filters { get; set; } = new();
        [JsonRequired] public bool ShowUniqueFiles { get; set; }
        [JsonRequired] public int MatchCount { get; set; }
        public string? SelectedLeftFolder { get; set; }
        public string? SelectedRightFolder { get; set; }
    }

    internal sealed class StoredFolder
    {
        [JsonRequired] public int Root { get; set; }
        [JsonRequired] public string Path { get; set; } = "";
        [JsonRequired] public long TotalSize { get; set; }
        public DateTime? LatestModificationDate { get; set; }
        [JsonRequired] public List<StoredFile> Files { get; set; } = new();
    }

    internal sealed class StoredFile
    {
        [JsonRequired] public string Name { get; set; } = "";
        public long? Size { get; set; }
        public DateTime? LastWriteTime { get; set; }
        public string? Hash { get; set; }
    }

    // Track field presence while deserializing older JSON files directly from the
    // stream. This preserves validation without retaining a second, huge JSON DOM.
    internal sealed class LegacyProjectSnapshot
    {
        [JsonIgnore] public ProjectData Data { get; } = new();
        [JsonIgnore] public HashSet<string> Present { get; } = new(StringComparer.OrdinalIgnoreCase);
        [JsonRequired] public List<string> ScanFolders
        {
            get => Data.ScanFolders;
            set { Data.ScanFolders = value; Present.Add(nameof(ScanFolders)); }
        }
        public Dictionary<string, List<string>> FolderFileCache
        {
            get => Data.FolderFileCache;
            set { Data.FolderFileCache = value; Present.Add(nameof(FolderFileCache)); }
        }
        public Dictionary<string, FolderInfo> FolderInfoCache
        {
            get => Data.FolderInfoCache;
            set { Data.FolderInfoCache = value; Present.Add(nameof(FolderInfoCache)); }
        }
        public Dictionary<string, FileMetadata> FileMetadataCache
        {
            get => Data.FileMetadataCache;
            set { Data.FileMetadataCache = value; Present.Add(nameof(FileMetadataCache)); }
        }
        public Dictionary<string, string> FileHashCache
        {
            get => Data.FileHashCache;
            set { Data.FileHashCache = value; Present.Add(nameof(FileHashCache)); }
        }
        public List<FileMatch> DuplicateFiles
        {
            get => Data.DuplicateFiles;
            set { Data.DuplicateFiles = value; Present.Add(nameof(DuplicateFiles)); }
        }
        public DateTime CreatedDate
        {
            get => Data.CreatedDate;
            set { Data.CreatedDate = value; Present.Add(nameof(CreatedDate)); }
        }
        public string ApplicationName
        {
            get => Data.ApplicationName;
            set { Data.ApplicationName = value; Present.Add(nameof(ApplicationName)); }
        }
        public string Version
        {
            get => Data.Version;
            set { Data.Version = value; Present.Add(nameof(Version)); }
        }
        public bool HasAnalysis
        {
            get => Data.HasAnalysis;
            set { Data.HasAnalysis = value; Present.Add(nameof(HasAnalysis)); }
        }
        public FilterCriteria Filters
        {
            get => Data.Filters;
            set { Data.Filters = value; Present.Add(nameof(Filters)); }
        }
        public bool ShowUniqueFiles
        {
            get => Data.ShowUniqueFiles;
            set { Data.ShowUniqueFiles = value; Present.Add(nameof(ShowUniqueFiles)); }
        }
        public string? SelectedLeftFolder
        {
            get => Data.SelectedLeftFolder;
            set { Data.SelectedLeftFolder = value; Present.Add(nameof(SelectedLeftFolder)); }
        }
        public string? SelectedRightFolder
        {
            get => Data.SelectedRightFolder;
            set { Data.SelectedRightFolder = value; Present.Add(nameof(SelectedRightFolder)); }
        }
    }
}
