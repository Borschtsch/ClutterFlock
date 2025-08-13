using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Core
{
    /// <summary>
    /// Thread-safe cache manager for folder and file information
    /// </summary>
    public class CacheManager : ICacheManager
    {
        private readonly ConcurrentDictionary<string, FolderInfo> _folderInfoCache;
        private readonly ConcurrentDictionary<string, string> _fileHashCache;
        private readonly ConcurrentDictionary<string, List<string>> _folderFileCache;
        private readonly ConcurrentDictionary<string, FileMetadata> _fileMetadataCache;

        public CacheManager()
        {
            _folderInfoCache = new ConcurrentDictionary<string, FolderInfo>(StringComparer.OrdinalIgnoreCase);
            _fileHashCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _folderFileCache = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _fileMetadataCache = new ConcurrentDictionary<string, FileMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsFolderCached(string folderPath)
        {
            return _folderInfoCache.ContainsKey(folderPath);
        }

        public void CacheFolderInfo(string folderPath, FolderInfo info)
        {
            _folderInfoCache[folderPath] = info;
            _folderFileCache[folderPath] = info.Files;
        }

        public FolderInfo? GetFolderInfo(string folderPath)
        {
            return _folderInfoCache.TryGetValue(folderPath, out var info) ? info : null;
        }

        public void CacheFileHash(string filePath, string hash)
        {
            _fileHashCache[filePath] = hash;
        }

        public string? GetFileHash(string filePath)
        {
            return _fileHashCache.TryGetValue(filePath, out var hash) ? hash : null;
        }

        public void ClearCache()
        {
            _folderInfoCache.Clear();
            _fileHashCache.Clear();
            _folderFileCache.Clear();
            _fileMetadataCache.Clear();
        }

        public void RemoveFolderFromCache(string folderPath)
        {
            // Normalize the folder path to ensure consistent comparison
            var normalizedPath = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            var keysToRemove = _folderInfoCache.Keys
                .Where(k => k.StartsWith(normalizedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _folderInfoCache.TryRemove(key, out _);
                _folderFileCache.TryRemove(key, out _);
            }

            // Also remove file hashes and metadata for files in removed folders
            var fileKeysToRemove = _fileHashCache.Keys
                .Where(k => k.StartsWith(normalizedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in fileKeysToRemove)
            {
                _fileHashCache.TryRemove(key, out _);
                _fileMetadataCache.TryRemove(key, out _);
            }
        }

        public Dictionary<string, FolderInfo> GetAllFolderInfo()
        {
            return _folderInfoCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public Dictionary<string, string> GetAllFileHashes()
        {
            return _fileHashCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public Dictionary<string, List<string>> GetAllFolderFiles()
        {
            return _folderFileCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public List<string> GetFolderFiles(string folderPath)
        {
            return _folderFileCache.TryGetValue(folderPath, out var files) ? files : new List<string>();
        }

        public long GetFolderSize(string folderPath)
        {
            return _folderInfoCache.TryGetValue(folderPath, out var info) ? info.TotalSize : 0;
        }

        public int GetCachedFolderCount()
        {
            return _folderInfoCache.Count;
        }

        public int GetCachedFileHashCount()
        {
            return _fileHashCache.Count;
        }

        public void CacheFileMetadata(string filePath, FileMetadata metadata)
        {
            _fileMetadataCache[filePath] = metadata;
        }

        public FileMetadata? GetFileMetadata(string filePath)
        {
            return _fileMetadataCache.TryGetValue(filePath, out var metadata) ? metadata : null;
        }

        public void LoadFromProjectData(ProjectData projectData)
        {
            ClearCache();

            // Load folder info cache
            foreach (var kvp in projectData.FolderInfoCache)
            {
                _folderInfoCache[kvp.Key] = kvp.Value;
            }

            // Load file hash cache
            foreach (var kvp in projectData.FileHashCache)
            {
                _fileHashCache[kvp.Key] = kvp.Value;
            }

            // Load folder file cache
            foreach (var kvp in projectData.FolderFileCache)
            {
                _folderFileCache[kvp.Key] = kvp.Value;
            }

            // Rebuild file metadata cache from folder info
            foreach (var folderInfo in projectData.FolderInfoCache.Values)
            {
                foreach (var filePath in folderInfo.Files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Exists)
                        {
                            var metadata = new FileMetadata
                            {
                                FileName = fileInfo.Name,
                                Size = fileInfo.Length,
                                LastWriteTime = fileInfo.LastWriteTime
                            };
                            _fileMetadataCache[filePath] = metadata;
                        }
                    }
                    catch
                    {
                        // Skip files that can't be accessed
                        continue;
                    }
                }
            }
        }

        public ProjectData ExportToProjectData(List<string> scanFolders)
        {
            return new ProjectData
            {
                ScanFolders = scanFolders,
                FolderInfoCache = GetAllFolderInfo(),
                FileHashCache = GetAllFileHashes(),
                FolderFileCache = GetAllFolderFiles(),
                CreatedDate = DateTime.Now
                // Note: Version, ApplicationName, and LegacyApplicationName should be set by ProjectManager
            };
        }
    }
}
