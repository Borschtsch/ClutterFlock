using System;
using System.Collections.Generic;
using System.IO;

namespace ClutterFlock.Models
{
    /// <summary>
    /// Represents a pair of duplicate files
    /// </summary>
    public sealed record FileMatch(string PathA, string PathB);

    /// <summary>
    /// Represents a pair of folders with their duplicate files and similarity metrics
    /// </summary>
    public sealed class FolderMatch
    {
        public string LeftFolder { get; }
        public string RightFolder { get; }
        public List<FileMatch> DuplicateFiles { get; }
        /// <summary>
        /// Jaccard similarity percentage between the two folders (0-100%)
        /// Calculated as: (duplicate files count / union of all files) * 100
        /// </summary>
        public double SimilarityPercentage { get; }
        public long FolderSizeBytes { get; }
        public string FolderName => Path.GetFileName(LeftFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        public string SizeDisplay => FormatSize(FolderSizeBytes);
        public DateTime? LatestModificationDate { get; set; }

        /// <summary>
        /// Initializes a new FolderMatch with corrected Jaccard similarity calculation
        /// </summary>
        /// <param name="leftFolder">Path to the left folder</param>
        /// <param name="rightFolder">Path to the right folder</param>
        /// <param name="duplicateFiles">List of duplicate files found between the folders</param>
        /// <param name="totalLeftFiles">Total number of files in the left folder</param>
        /// <param name="totalRightFiles">Total number of files in the right folder</param>
        /// <param name="folderSizeBytes">Size of the folder in bytes</param>
        public FolderMatch(string leftFolder, string rightFolder, List<FileMatch> duplicateFiles, 
            int totalLeftFiles, int totalRightFiles, long folderSizeBytes = 0)
        {
            LeftFolder = leftFolder;
            RightFolder = rightFolder;
            DuplicateFiles = duplicateFiles;
            FolderSizeBytes = folderSizeBytes;
            
            // Calculate Jaccard similarity: |A ∩ B| / |A ∪ B|
            // This represents the ratio of shared files to total unique files across both folders
            // Union size = total files in both folders minus duplicates (to avoid double counting)
            var unionSize = totalLeftFiles + totalRightFiles - duplicateFiles.Count;
            SimilarityPercentage = unionSize > 0 
                ? (duplicateFiles.Count / (double)unionSize * 100.0) 
                : 0.0;
        }

        private static string FormatSize(long size)
        {
            if (size >= 1L << 30) return $"{size / (1L << 30):N1} GB";
            if (size >= 1L << 20) return $"{size / (1L << 20):N1} MB";
            if (size >= 1L << 10) return $"{size / (1L << 10):N1} KB";
            return $"{size} B";
        }
    }

    /// <summary>
    /// Comprehensive file information for comparison view
    /// </summary>
    public class FileDetailInfo
    {
        public string LeftFileName { get; set; } = string.Empty;
        public string LeftSizeDisplay { get; set; } = string.Empty;
        public long LeftSizeBytes { get; set; }
        public string LeftDateDisplay { get; set; } = string.Empty;
        public DateTime? LeftDate { get; set; }
        public string LeftFullPath { get; set; } = string.Empty;
        
        public string RightFileName { get; set; } = string.Empty;
        public string RightSizeDisplay { get; set; } = string.Empty;
        public long RightSizeBytes { get; set; }
        public string RightDateDisplay { get; set; } = string.Empty;
        public DateTime? RightDate { get; set; }
        public string RightFullPath { get; set; } = string.Empty;
        
        public bool IsDuplicate { get; set; }
        public bool HasLeftFile => !string.IsNullOrEmpty(LeftFileName);
        public bool HasRightFile => !string.IsNullOrEmpty(RightFileName);
        public string PrimaryFileName => HasLeftFile ? LeftFileName : RightFileName;
        
        public string TooltipText
        {
            get
            {
                if (HasLeftFile && HasRightFile)
                    return $"Left: {LeftFullPath} ({LeftDateDisplay})\nRight: {RightFullPath} ({RightDateDisplay})";
                else if (HasLeftFile)
                    return $"Left only: {LeftFullPath} ({LeftDateDisplay})";
                else
                    return $"Right only: {RightFullPath} ({RightDateDisplay})";
            }
        }
    }

    /// <summary>
    /// Metadata for individual files (cached to avoid file system access)
    /// </summary>
    public class FileMetadata
    {
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastWriteTime { get; set; }
    }

    /// <summary>
    /// Folder information cache entry
    /// </summary>
    public class FolderInfo
    {
        public List<string> Files { get; set; } = new();
        public long TotalSize { get; set; }
        public int FileCount => Files.Count;
        public DateTime? LatestModificationDate { get; set; }
    }

    /// <summary>
    /// Project data for persistence
    /// </summary>
    public class ProjectData
    {
        public List<string> ScanFolders { get; set; } = new();
        public Dictionary<string, List<string>> FolderFileCache { get; set; } = new();
        public Dictionary<string, string> FileHashCache { get; set; } = new();
        public Dictionary<string, FolderInfo> FolderInfoCache { get; set; } = new();
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public string Version { get; set; } = "1.0";
        public string ApplicationName { get; set; } = "ClutterFlock";
    }

    /// <summary>
    /// Analysis progress information
    /// </summary>
    public class AnalysisProgress
    {
        public int CurrentProgress { get; set; }
        public int MaxProgress { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public AnalysisPhase Phase { get; set; }
        public bool IsIndeterminate { get; set; }
    }

    /// <summary>
    /// Analysis phase enumeration
    /// </summary>
    public enum AnalysisPhase
    {
        Idle,
        CountingFolders,
        ScanningFolders,
        BuildingFileIndex,
        ComparingFiles,
        AggregatingResults,
        PopulatingResults,
        Complete,
        Cancelled,
        Error
    }

    /// <summary>
    /// Filter criteria for results
    /// </summary>
    public class FilterCriteria
    {
        public double MinimumSimilarityPercent { get; set; } = 50.0;
        public long MinimumSizeBytes { get; set; } = 1024 * 1024; // 1MB
        public DateTime? MinimumDate { get; set; }
        public DateTime? MaximumDate { get; set; }
        public List<string> FileExtensions { get; set; } = new();
    }

    /// <summary>
    /// Represents a recovery action for error handling
    /// </summary>
    public class RecoveryAction
    {
        public RecoveryActionType Type { get; set; }
        public string Message { get; set; } = string.Empty;
        public string SuggestedSolution { get; set; } = string.Empty;
        public bool ShouldRetry { get; set; }
        public TimeSpan RetryDelay { get; set; } = TimeSpan.Zero;
    }

    /// <summary>
    /// Types of recovery actions available
    /// </summary>
    public enum RecoveryActionType
    {
        Skip,
        Retry,
        RetryWithElevation,
        ReduceParallelism,
        PauseAndWait,
        Abort
    }

    /// <summary>
    /// Types of resource constraints that can occur
    /// </summary>
    public enum ResourceConstraintType
    {
        Memory,
        DiskSpace,
        FileHandles,
        NetworkBandwidth,
        CpuUsage
    }

    /// <summary>
    /// Summary of errors encountered during analysis
    /// </summary>
    public class ErrorSummary
    {
        public int SkippedFiles { get; set; }
        public int PermissionErrors { get; set; }
        public int NetworkErrors { get; set; }
        public int ResourceErrors { get; set; }
        public List<string> SkippedPaths { get; set; } = new();
        public List<string> ErrorMessages { get; set; } = new();
        public DateTime LastErrorTime { get; set; }
        
        public bool HasErrors => SkippedFiles > 0 || PermissionErrors > 0 || NetworkErrors > 0 || ResourceErrors > 0;
        public int TotalErrors => PermissionErrors + NetworkErrors + ResourceErrors;
    }
}
