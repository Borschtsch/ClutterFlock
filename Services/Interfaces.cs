using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClutterFlock.Models;

namespace ClutterFlock.Services
{
    /// <summary>
    /// Interface for folder scanning operations
    /// </summary>
    public interface IFolderScanner
    {
        Task<List<string>> ScanFolderHierarchyAsync(string rootPath, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken);
        Task<FolderInfo> AnalyzeFolderAsync(string folderPath, CancellationToken cancellationToken);
        int CountSubfolders(string rootPath);
    }

    /// <summary>
    /// Interface for cache management operations
    /// </summary>
    public interface ICacheManager
    {
        bool IsFolderCached(string folderPath);
        void CacheFolderInfo(string folderPath, FolderInfo info);
        FolderInfo? GetFolderInfo(string folderPath);
        void CacheFileHash(string filePath, string hash);
        string? GetFileHash(string filePath);
        void ClearCache();
        void RemoveFolderFromCache(string folderPath);
        Dictionary<string, FolderInfo> GetAllFolderInfo();
        Dictionary<string, string> GetAllFileHashes();
        Dictionary<string, List<string>> GetAllFolderFiles();
        List<string> GetFolderFiles(string folderPath);
        long GetFolderSize(string folderPath);
        int GetCachedFolderCount();
        int GetCachedFileHashCount();
        void CacheFileMetadata(string filePath, FileMetadata metadata);
        FileMetadata? GetFileMetadata(string filePath);
        void LoadFromProjectData(ProjectData projectData);
        ProjectData ExportToProjectData(List<string> scanFolders);
    }

    /// <summary>
    /// Interface for duplicate analysis operations
    /// </summary>
    public interface IDuplicateAnalyzer
    {
        Task<List<FileMatch>> FindDuplicateFilesAsync(List<string> folders, IProgress<AnalysisProgress>? progress, CancellationToken cancellationToken);
        List<FolderMatch> AggregateFolderMatches(List<FileMatch> fileMatches, ICacheManager cacheManager);
        Task<List<FolderMatch>> AggregateFolderMatchesAsync(List<FileMatch> fileMatches, ICacheManager cacheManager, IProgress<AnalysisProgress>? progress = null);
        List<FolderMatch> ApplyFilters(List<FolderMatch> matches, FilterCriteria criteria);
        Task<string> ComputeFileHashAsync(string filePath);
    }

    /// <summary>
    /// Interface for project persistence operations
    /// </summary>
    public interface IProjectManager
    {
        Task SaveProjectAsync(string filePath, ProjectData projectData);
        Task<ProjectData> LoadProjectAsync(string filePath);
        bool IsValidProjectFile(string filePath);
    }

    /// <summary>
    /// Interface for file comparison operations
    /// </summary>
    public interface IFileComparer
    {
        List<FileDetailInfo> BuildFileComparison(string leftFolder, string rightFolder, 
            List<FileMatch> duplicateFiles, ICacheManager cacheManager);
        List<FileDetailInfo> FilterFileDetails(List<FileDetailInfo> allFiles, bool showUniqueFiles);
    }

    /// <summary>
    /// Interface for progress reporting
    /// </summary>
    public interface IProgressReporter
    {
        event EventHandler<AnalysisProgress> ProgressChanged;
        void ReportProgress(AnalysisProgress progress);
        void ReportProgress(int current, int max, string message, AnalysisPhase phase);
    }

    /// <summary>
    /// Interface for comprehensive error recovery and management
    /// </summary>
    public interface IErrorRecoveryService
    {
        Task<RecoveryAction> HandleFileAccessError(string filePath, Exception error);
        Task<RecoveryAction> HandleNetworkError(string networkPath, Exception error);
        Task<RecoveryAction> HandleResourceConstraintError(ResourceConstraintType type, Exception error);
        void LogSkippedItem(string path, string reason);
        ErrorSummary GetErrorSummary();
        void ClearErrorSummary();
    }
}
