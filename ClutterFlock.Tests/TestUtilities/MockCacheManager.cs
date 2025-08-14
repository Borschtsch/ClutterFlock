using System;
using System.Collections.Generic;
using System.Linq;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Tests.TestUtilities
{
    /// <summary>
    /// Mock implementation of ICacheManager for testing
    /// </summary>
    public class MockCacheManager : ICacheManager
    {
        private readonly Dictionary<string, FolderInfo> _folderInfoCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _fileHashCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _folderFilesCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileMetadata> _fileMetadataCache = new(StringComparer.OrdinalIgnoreCase);

        // Test utilities
        public List<string> CacheOperations { get; } = new();
        public bool ThrowOnNextOperation { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public bool IsFolderCached(string folderPath)
        {
            RecordOperation($"IsFolderCached({folderPath})");
            ThrowIfConfigured();
            return _folderInfoCache.ContainsKey(folderPath);
        }

        public void CacheFolderInfo(string folderPath, FolderInfo info)
        {
            RecordOperation($"CacheFolderInfo({folderPath}, {info.FileCount} files)");
            ThrowIfConfigured();
            _folderInfoCache[folderPath] = info;
        }

        public FolderInfo? GetFolderInfo(string folderPath)
        {
            RecordOperation($"GetFolderInfo({folderPath})");
            ThrowIfConfigured();
            return _folderInfoCache.TryGetValue(folderPath, out var info) ? info : null;
        }

        public void CacheFileHash(string filePath, string hash)
        {
            RecordOperation($"CacheFileHash({filePath}, {hash})");
            ThrowIfConfigured();
            _fileHashCache[filePath] = hash;
        }

        public string? GetFileHash(string filePath)
        {
            RecordOperation($"GetFileHash({filePath})");
            ThrowIfConfigured();
            return _fileHashCache.TryGetValue(filePath, out var hash) ? hash : null;
        }

        public void ClearCache()
        {
            RecordOperation("ClearCache()");
            ThrowIfConfigured();
            _folderInfoCache.Clear();
            _fileHashCache.Clear();
            _folderFilesCache.Clear();
            _fileMetadataCache.Clear();
        }

        public void RemoveFolderFromCache(string folderPath)
        {
            RecordOperation($"RemoveFolderFromCache({folderPath})");
            ThrowIfConfigured();
            _folderInfoCache.Remove(folderPath);
            _folderFilesCache.Remove(folderPath);
            
            // Remove all files in this folder from caches
            var filesToRemove = _fileHashCache.Keys
                .Where(f => f.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            foreach (var file in filesToRemove)
            {
                _fileHashCache.Remove(file);
                _fileMetadataCache.Remove(file);
            }
        }

        public Dictionary<string, FolderInfo> GetAllFolderInfo()
        {
            RecordOperation("GetAllFolderInfo()");
            ThrowIfConfigured();
            return new Dictionary<string, FolderInfo>(_folderInfoCache);
        }

        public Dictionary<string, string> GetAllFileHashes()
        {
            RecordOperation("GetAllFileHashes()");
            ThrowIfConfigured();
            return new Dictionary<string, string>(_fileHashCache);
        }

        public Dictionary<string, List<string>> GetAllFolderFiles()
        {
            RecordOperation("GetAllFolderFiles()");
            ThrowIfConfigured();
            return new Dictionary<string, List<string>>(_folderFilesCache);
        }

        public List<string> GetFolderFiles(string folderPath)
        {
            RecordOperation($"GetFolderFiles({folderPath})");
            ThrowIfConfigured();
            return _folderFilesCache.TryGetValue(folderPath, out var files) ? new List<string>(files) : new List<string>();
        }

        public long GetFolderSize(string folderPath)
        {
            RecordOperation($"GetFolderSize({folderPath})");
            ThrowIfConfigured();
            return _folderInfoCache.TryGetValue(folderPath, out var info) ? info.TotalSize : 0;
        }

        public int GetCachedFolderCount()
        {
            RecordOperation("GetCachedFolderCount()");
            ThrowIfConfigured();
            return _folderInfoCache.Count;
        }

        public int GetCachedFileHashCount()
        {
            RecordOperation("GetCachedFileHashCount()");
            ThrowIfConfigured();
            return _fileHashCache.Count;
        }

        public void CacheFileMetadata(string filePath, FileMetadata metadata)
        {
            RecordOperation($"CacheFileMetadata({filePath}, {metadata.Size} bytes)");
            ThrowIfConfigured();
            _fileMetadataCache[filePath] = metadata;
        }

        public FileMetadata? GetFileMetadata(string filePath)
        {
            RecordOperation($"GetFileMetadata({filePath})");
            ThrowIfConfigured();
            return _fileMetadataCache.TryGetValue(filePath, out var metadata) ? metadata : null;
        }

        public void LoadFromProjectData(ProjectData projectData)
        {
            RecordOperation($"LoadFromProjectData({projectData.ScanFolders.Count} folders)");
            ThrowIfConfigured();
            
            _folderInfoCache.Clear();
            _fileHashCache.Clear();
            _folderFilesCache.Clear();
            
            foreach (var kvp in projectData.FolderInfoCache)
            {
                _folderInfoCache[kvp.Key] = kvp.Value;
            }
            
            foreach (var kvp in projectData.FileHashCache)
            {
                _fileHashCache[kvp.Key] = kvp.Value;
            }
            
            foreach (var kvp in projectData.FolderFileCache)
            {
                _folderFilesCache[kvp.Key] = kvp.Value;
            }
        }

        public ProjectData ExportToProjectData(List<string> scanFolders)
        {
            RecordOperation($"ExportToProjectData({scanFolders.Count} folders)");
            ThrowIfConfigured();
            
            return new ProjectData
            {
                ScanFolders = scanFolders,
                FolderInfoCache = new Dictionary<string, FolderInfo>(_folderInfoCache),
                FileHashCache = new Dictionary<string, string>(_fileHashCache),
                FolderFileCache = new Dictionary<string, List<string>>(_folderFilesCache),
                CreatedDate = DateTime.Now,
                Version = "1.0",
                ApplicationName = "ClutterFlock"
            };
        }

        // Test utility methods
        public void SetupFolderInfo(string folderPath, FolderInfo info)
        {
            _folderInfoCache[folderPath] = info;
        }

        public void SetupFileHash(string filePath, string hash)
        {
            _fileHashCache[filePath] = hash;
        }

        public void SetupFolderFiles(string folderPath, List<string> files)
        {
            _folderFilesCache[folderPath] = files;
        }

        public void ClearOperationLog()
        {
            CacheOperations.Clear();
        }

        public bool WasOperationCalled(string operation)
        {
            return CacheOperations.Any(op => op.Contains(operation));
        }

        public int GetOperationCount(string operation)
        {
            return CacheOperations.Count(op => op.Contains(operation));
        }

        private void RecordOperation(string operation)
        {
            CacheOperations.Add($"{DateTime.Now:HH:mm:ss.fff}: {operation}");
        }

        private void ThrowIfConfigured()
        {
            if (ThrowOnNextOperation)
            {
                ThrowOnNextOperation = false;
                var exception = ExceptionToThrow ?? new InvalidOperationException("Mock exception");
                ExceptionToThrow = null;
                throw exception;
            }
        }
    }
}