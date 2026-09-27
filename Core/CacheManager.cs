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
            // Normalize the folder path to ensure consistent comparison.
            // PathUtilities also checks directory boundaries before removing descendants.
            RemoveEntries(path => PathUtilities.IsWithin(path, folderPath));
        }

        public void RemoveFileFromCache(string filePath)
        {
            _fileHashCache.TryRemove(filePath, out _);
            _fileMetadataCache.TryRemove(filePath, out _);
            var folder = Path.GetDirectoryName(filePath)!;
            if (!_folderInfoCache.TryGetValue(folder, out var previous)) return;
            var files = previous.Files.Where(p => !p.Equals(filePath, StringComparison.OrdinalIgnoreCase)).ToList();
            CacheFolderInfo(folder, new FolderInfo
            {
                Files = files,
                TotalSize = files.Sum(p => GetFileMetadata(p)?.Size ?? 0),
                LatestModificationDate = files.Select(p => (DateTime?)GetFileMetadata(p)?.LastWriteTime).DefaultIfEmpty().Max()
            });
        }

        public void RetainFolders(IReadOnlyCollection<string> roots)
        {
            RemoveEntries(path => !roots.Any(root => PathUtilities.IsWithin(path, root)));
        }

        private void RemoveEntries(Func<string, bool> remove)
        {
            foreach (var path in _folderInfoCache.Keys.Where(remove))
            {
                _folderInfoCache.TryRemove(path, out _);
                _folderFileCache.TryRemove(path, out _);
            }
            // Also remove file hashes and metadata for files in removed folders.
            // Metadata has its own keys because not every scanned file has been hashed.
            foreach (var path in _fileHashCache.Keys.Where(remove))
                _fileHashCache.TryRemove(path, out _);
            foreach (var path in _fileMetadataCache.Keys.Where(remove))
                _fileMetadataCache.TryRemove(path, out _);
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

            // Load folder file cache from the authoritative folder information.
            foreach (var entry in projectData.FolderInfoCache)
                _folderFileCache[entry.Key] = entry.Value.Files;

            // Load file hash cache
            foreach (var kvp in projectData.FileHashCache)
            {
                _fileHashCache[kvp.Key] = kvp.Value;
            }

            // Rebuild the file metadata cache from the saved metadata, rather than
            // rereading folder files that may no longer be accessible.
            // Restore the saved snapshot without touching potentially disconnected drives.
            foreach (var entry in projectData.FileMetadataCache)
                _fileMetadataCache[entry.Key] = entry.Value;
        }

        public ProjectData ExportToProjectData(List<string> scanFolders)
        {
            return new ProjectData
            {
                ScanFolders = scanFolders.ToList(),
                FolderInfoCache = GetAllFolderInfo(),
                FileHashCache = GetAllFileHashes(),
                FileMetadataCache = _fileMetadataCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                FolderFileCache = GetAllFolderFiles(),
                CreatedDate = DateTime.Now
                // Note: Version and ApplicationName are set by ProjectManager.
            };
        }
    }
}
