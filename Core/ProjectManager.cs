using System.IO;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text.Json.Serialization;
using System.Text.Json;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Core
{
    /// <summary>
    /// Handles project persistence operations
    /// </summary>
    public class ProjectManager : IProjectManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public async Task SaveProjectAsync(string filePath, ProjectData projectData)
        {
            ArgumentNullException.ThrowIfNull(projectData);
            // Always set the current application name
            projectData.ApplicationName = "ClutterFlock";
            projectData.Version = "3.0";
            var fileIds = Validate(projectData);
            var destination = Path.GetFullPath(filePath);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                // Commit only after the entire snapshot has reached disk. A failed save
                // leaves an existing project intact; the temporary is on the same volume.
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    // Store each filename/metadata record once under its folder. Compress
                    // each entry while writing; do not create a giant intermediate JSON string.
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        var header = new StoredProject
                        {
                            ScanFolders = projectData.ScanFolders,
                            CreatedDate = projectData.CreatedDate,
                            HasAnalysis = projectData.HasAnalysis,
                            Filters = projectData.Filters,
                            ShowUniqueFiles = projectData.ShowUniqueFiles,
                            SelectedLeftFolder = projectData.SelectedLeftFolder,
                            SelectedRightFolder = projectData.SelectedRightFolder,
                            MatchCount = projectData.DuplicateFiles.Count,
                            Workspace = projectData.Workspace
                        };
                        await using (var entry = archive.CreateEntry("project.json", CompressionLevel.Optimal).Open())
                            await JsonSerializer.SerializeAsync(entry, header, JsonOptions);
                        await using (var entry = archive.CreateEntry("folders.json", CompressionLevel.Optimal).Open())
                            await JsonSerializer.SerializeAsync(entry, EnumerateStoredFolders(projectData), JsonOptions);
                        await using (var entry = archive.CreateEntry("matches.bin", CompressionLevel.Optimal).Open())
                        {
                            // Each match uses two 32-bit file IDs instead of two full paths.
                            // IDs follow file order in folders.json. Batch writes to the compressor.
                            var buffer = new byte[65536];
                            var used = 0;
                            foreach (var match in projectData.DuplicateFiles)
                            {
                                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(used), fileIds[match.PathA]);
                                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(used + 4), fileIds[match.PathB]);
                                used += 8;
                                if (used == buffer.Length)
                                {
                                    await entry.WriteAsync(buffer);
                                    used = 0;
                                }
                            }
                            if (used > 0) await entry.WriteAsync(buffer.AsMemory(0, used));
                        }
                    }
                    await stream.FlushAsync();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public async Task<ProjectData> LoadProjectAsync(string filePath, CancellationToken cancellationToken = default)
        {
            await using var stream = File.OpenRead(filePath);
            var signature = new byte[4];
            var bytesRead = await stream.ReadAsync(signature, cancellationToken);
            stream.Position = 0;
            if (bytesRead == 4 && signature.AsSpan().SequenceEqual(new byte[] { 0x50, 0x4b, 3, 4 }))
                return await LoadCompactAsync(stream, cancellationToken);

            // Default-initialized DTO properties must not make arbitrary JSON a project.
            // The legacy reader requires scanFolders and tracks the version 2 fields.
            // Handle legacy project files that don't have application identification fields:
            // ProjectData supplies the default application name during deserialization.
            var legacy = await JsonSerializer.DeserializeAsync<LegacyProjectSnapshot>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Empty project data.");
            var data = legacy.Data;
            if (data.Version == "2.0")
            {
                var required = new[] { "applicationName", "createdDate", "folderInfoCache", "fileMetadataCache",
                    "fileHashCache", "duplicateFiles", "hasAnalysis", "filters", "showUniqueFiles" };
                if (required.Any(name => !legacy.Present.Contains(name)))
                    throw new InvalidDataException("Incomplete version 2 project snapshot.");
            }
            if (data.Version != null && data.Version.StartsWith("3.", StringComparison.Ordinal))
                throw new InvalidDataException("Version 3 projects must use the compact container.");
            Validate(data);
            // Older projects only saved caches. Their matches can still be reconstructed
            // from saved hashes, including when the original drives are offline.
            if (data.Version!.StartsWith("1.", StringComparison.Ordinal))
            {
                foreach (var entry in data.FolderFileCache)
                    data.FolderInfoCache.TryAdd(entry.Key, new FolderInfo { Files = entry.Value });
                data.FileHashCache = new(data.FileHashCache, StringComparer.OrdinalIgnoreCase);
                var groups = data.FolderInfoCache.Values.SelectMany(f => f.Files)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(f => data.FileHashCache.ContainsKey(f))
                    .GroupBy(f => (Name: Path.GetFileName(f).ToUpperInvariant(), Hash: data.FileHashCache[f]));
                data.DuplicateFiles.Clear();
                foreach (var group in groups)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var files = group.ToList();
                    for (var i = 0; i < files.Count; i++)
                        for (var j = i + 1; j < files.Count; j++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!string.Equals(Path.GetDirectoryName(files[i]), Path.GetDirectoryName(files[j]), StringComparison.OrdinalIgnoreCase))
                                data.DuplicateFiles.Add(new(files[i], files[j]));
                        }
                }
                data.HasAnalysis = data.FileHashCache.Count > 0;
            }
            return data;
        }

        public bool IsValidProjectFile(string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath);
                if (!extension.Equals(".cfp", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".dfp", StringComparison.OrdinalIgnoreCase)) return false;
                // Validation is identical to loading; no separate permissive parser.
                Task.Run(() => LoadProjectAsync(filePath)).GetAwaiter().GetResult();
                return true;
            }
            catch { return false; }
        }

        private static IEnumerable<StoredFolder> EnumerateStoredFolders(ProjectData data)
        {
            foreach (var entry in data.FolderInfoCache)
            {
                var root = data.ScanFolders.FindIndex(r => PathUtilities.IsWithin(entry.Key, r));
                var folder = new StoredFolder
                {
                    Root = root,
                    Path = Path.GetRelativePath(data.ScanFolders[root], entry.Key),
                    TotalSize = entry.Value.TotalSize,
                    LatestModificationDate = entry.Value.LatestModificationDate
                };
                foreach (var file in entry.Value.Files)
                {
                    data.FileMetadataCache.TryGetValue(file, out var metadata);
                    data.FileHashCache.TryGetValue(file, out var hash);
                    folder.Files.Add(new StoredFile
                    {
                        Name = Path.GetFileName(file),
                        Size = metadata?.Size,
                        LastWriteTime = metadata?.LastWriteTime,
                        Hash = hash, ContentSample = metadata?.ContentSample
                    });
                }
                yield return folder;
            }
        }

        private static async Task<ProjectData> LoadCompactAsync(Stream stream, CancellationToken token)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count != 3 || archive.Entries.Select(e => e.FullName).Distinct().Count() != 3)
                throw new InvalidDataException("Invalid project container entries.");
            ZipArchiveEntry Entry(string name) => archive.GetEntry(name)
                ?? throw new InvalidDataException($"Project is missing {name}.");
            StoredProject header;
            await using (var input = Entry("project.json").Open())
                header = await JsonSerializer.DeserializeAsync<StoredProject>(input, JsonOptions, token)
                    ?? throw new InvalidDataException("Missing project header.");
            if (header.Version != "3.0" || header.MatchCount < 0)
                throw new InvalidDataException("Unsupported project format or match count.");
            var data = new ProjectData
            {
                Workspace = header.Workspace,
                ApplicationName = header.ApplicationName,
                Version = header.Version,
                ScanFolders = header.ScanFolders,
                CreatedDate = header.CreatedDate,
                HasAnalysis = header.HasAnalysis,
                Filters = header.Filters,
                ShowUniqueFiles = header.ShowUniqueFiles,
                SelectedLeftFolder = header.SelectedLeftFolder,
                SelectedRightFolder = header.SelectedRightFolder,
                FolderInfoCache = new(StringComparer.OrdinalIgnoreCase),
                FolderFileCache = new(StringComparer.OrdinalIgnoreCase),
                FileMetadataCache = new(StringComparer.OrdinalIgnoreCase),
                FileHashCache = new(StringComparer.OrdinalIgnoreCase)
            };
            Validate(data);
            // Root indices refer to the header's original order, before normalization/deduplication.
            var roots = header.ScanFolders;
            var files = new List<string>();
            var repeatedFolders = false;
            await using (var input = Entry("folders.json").Open())
            {
                await foreach (var folder in JsonSerializer.DeserializeAsyncEnumerable<StoredFolder>(input, JsonOptions, token))
                {
                    token.ThrowIfCancellationRequested();
                    if (folder == null || folder.Root < 0 || folder.Root >= roots.Count ||
                        string.IsNullOrWhiteSpace(folder.Path) || Path.IsPathRooted(folder.Path) || folder.Files == null)
                        throw new InvalidDataException("Invalid stored folder.");
                    var path = PathUtilities.Normalize(Path.Combine(roots[folder.Root], folder.Path));
                    if (!PathUtilities.IsWithin(path, roots[folder.Root]))
                        throw new InvalidDataException("Stored folder escapes its root.");
                    var repeated = data.FolderInfoCache.TryGetValue(path, out var info);
                    Dictionary<string, string>? originalFiles = null;
                    if (repeated)
                    {
                        // Older saves can record one directory through overlapping roots or path aliases.
                        // Only identical snapshots can be coalesced; never choose between conflicting evidence.
                        repeatedFolders = true;
                        if (info!.TotalSize != folder.TotalSize || info.LatestModificationDate != folder.LatestModificationDate ||
                            info.Files.Count != folder.Files.Count)
                            throw new InvalidDataException($"Conflicting stored folder records: {path}");
                        originalFiles = info.Files.ToDictionary(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        info = new FolderInfo { TotalSize = folder.TotalSize, LatestModificationDate = folder.LatestModificationDate };
                        data.FolderInfoCache.Add(path, info);
                        data.FolderFileCache.Add(path, info.Files);
                    }
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in folder.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        if (file == null || string.IsNullOrWhiteSpace(file.Name) || file.Name is "." or ".." ||
                            file.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !names.Add(file.Name) ||
                            file.Size.HasValue != file.LastWriteTime.HasValue)
                            throw new InvalidDataException("Invalid stored filename or metadata.");
                        var filePath = Path.Combine(path, file.Name);
                        if (repeated)
                        {
                            if (!originalFiles!.TryGetValue(file.Name, out var original))
                                throw new InvalidDataException($"Conflicting stored file lists: {path}");
                            data.FileMetadataCache.TryGetValue(original, out var metadata);
                            data.FileHashCache.TryGetValue(original, out var hash);
                            if (metadata?.Size != file.Size || metadata?.LastWriteTime != file.LastWriteTime ||
                                !string.Equals(metadata?.ContentSample, file.ContentSample, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(hash, file.Hash, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException($"Conflicting stored file evidence: {original}");
                            // Every stored occurrence still occupies an ID in matches.bin. Skipping it
                            // would shift later references and associate evidence with the wrong files.
                            files.Add(original);
                            continue;
                        }
                        info!.Files.Add(filePath);
                        files.Add(filePath);
                        if (file.Size.HasValue)
                            data.FileMetadataCache.Add(filePath, new FileMetadata
                            {
                                FileName = file.Name, Size = file.Size.Value, LastWriteTime = file.LastWriteTime!.Value, ContentSample = file.ContentSample
                            });
                        if (file.Hash != null) data.FileHashCache.Add(filePath, file.Hash);
                    }
                }
            }
            var matchesEntry = Entry("matches.bin");
            if (matchesEntry.Length != (long)header.MatchCount * 8)
                throw new InvalidDataException("Truncated or inconsistent saved matches.");
            await using (var input = matchesEntry.Open())
            {
                var buffer = new byte[65536];
                var remaining = header.MatchCount;
                while (remaining > 0)
                {
                    var count = Math.Min(remaining, buffer.Length / 8);
                    await input.ReadExactlyAsync(buffer.AsMemory(0, count * 8), token);
                    for (var i = 0; i < count; i++)
                    {
                        var left = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(i * 8));
                        var right = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(i * 8 + 4));
                        if (left < 0 || right < 0 || left >= files.Count || right >= files.Count)
                            throw new InvalidDataException("Unknown file reference in saved matches.");
                        data.DuplicateFiles.Add(new FileMatch(files[left], files[right]));
                    }
                    remaining -= count;
                }
                if (input.ReadByte() != -1) throw new InvalidDataException("Unexpected saved match data.");
            }
            Validate(data, coalesceRepeatedMatches: repeatedFolders);
            return data;
        }

        private static Dictionary<string, int> Validate(ProjectData data, bool coalesceRepeatedMatches = false)
        {
            if (data.ApplicationName != "ClutterFlock" ||
                !Version.TryParse(data.Version, out var version) || version.Major is < 1 or > 3)
                throw new InvalidDataException("Unsupported project application or format version.");
            if (data.ScanFolders == null || data.FolderInfoCache == null || data.FolderFileCache == null ||
                data.FileHashCache == null || data.FileMetadataCache == null || data.DuplicateFiles == null || data.Filters == null)
                throw new InvalidDataException("Project data contains missing collections or filters.");
            if (!double.IsFinite(data.Filters.MinimumSimilarityPercent) ||
                data.Filters.MinimumSimilarityPercent is < 0 or > 100 || data.Filters.MinimumSizeBytes < 0)
                throw new InvalidDataException("Invalid project filters.");
            static bool ValidPath(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);
            if (data.ScanFolders.Any(p => !ValidPath(p)))
                throw new InvalidDataException("Project roots must be absolute paths.");
            if (data.Workspace is { } workspace)
            {
                if (workspace.Locations == null || workspace.Reviews == null || workspace.AnalysisIssues == null ||
                    workspace.Locations.Any(l => l == null || !ValidPath(l.Path) || l.Label == null) ||
                    workspace.Reviews.Any(r => r == null || !ValidPath(r.LeftFolder) || !ValidPath(r.RightFolder) ||
                        r.Notes == null || r.Status is not ("Unreviewed" or "Reviewed" or "Investigate" or "Ignore")) ||
                    workspace.AnalysisIssues.Any(i => i == null) ||
                    !double.IsFinite(workspace.LocationsWidth) || !double.IsFinite(workspace.ComparisonsWidth))
                    throw new InvalidDataException("Invalid saved workspace.");
            }
            data.ScanFolders = data.ScanFolders.Select(PathUtilities.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            bool InRoots(string path) => ValidPath(path) && data.ScanFolders.Any(root => PathUtilities.IsWithin(path, root));
            bool InFolder(string file, string folder) => ValidPath(file) &&
                string.Equals(Path.GetDirectoryName(PathUtilities.Normalize(file)), PathUtilities.Normalize(folder), StringComparison.OrdinalIgnoreCase);
            var normalizedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in data.FolderInfoCache)
                if (!InRoots(entry.Key) || !normalizedFolders.Add(PathUtilities.Normalize(entry.Key)) || entry.Value == null || entry.Value.Files == null || entry.Value.TotalSize < 0 ||
                    entry.Value.Files.Any(f => !InFolder(f, entry.Key)) ||
                    entry.Value.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.Value.Files.Count)
                    throw new InvalidDataException("Invalid folder cache in project.");
            foreach (var entry in data.FolderFileCache)
                if (!InRoots(entry.Key) || entry.Value == null || entry.Value.Any(f => !InFolder(f, entry.Key)))
                    throw new InvalidDataException("Invalid file list in project.");
            foreach (var entry in data.FileMetadataCache)
                if (!InRoots(entry.Key) || entry.Value == null || entry.Value.Size < 0 ||
                    !Path.GetFileName(entry.Key).Equals(entry.Value.FileName, StringComparison.OrdinalIgnoreCase) ||
                    entry.Value.ContentSample is { } sample && (sample.Length != 67 || !sample.StartsWith("v1:", StringComparison.Ordinal) || !sample[3..].All(Uri.IsHexDigit)))
                    throw new InvalidDataException("Invalid file metadata in project.");
            foreach (var entry in data.FileHashCache)
                if (!InRoots(entry.Key) || entry.Value == null || entry.Value.Length != 64 || !entry.Value.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Invalid file hash in project.");
            var knownFiles = data.FolderInfoCache.Values.SelectMany(f => f.Files)
                .Select((path, index) => (path, index)).ToDictionary(f => f.path, f => f.index, StringComparer.OrdinalIgnoreCase);
            var hashes = new Dictionary<string, string>(data.FileHashCache, StringComparer.OrdinalIgnoreCase);
            // Integer pairs avoid allocating another pair of full paths per saved match.
            var pairs = new HashSet<(int, int)>();
            var retained = 0;
            for (var index = 0; index < data.DuplicateFiles.Count; index++)
            {
                var match = data.DuplicateFiles[index];
                if (match == null || !knownFiles.ContainsKey(match.PathA) || !knownFiles.ContainsKey(match.PathB) ||
                    !Path.GetFileName(match.PathA).Equals(Path.GetFileName(match.PathB), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetDirectoryName(match.PathA), Path.GetDirectoryName(match.PathB), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid analysis result in project.");
                if (!hashes.TryGetValue(match.PathA, out var leftHash) ||
                    !hashes.TryGetValue(match.PathB, out var rightHash) ||
                    !leftHash.Equals(rightHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Analysis result does not match saved file hashes.");
                var left = knownFiles[match.PathA];
                var right = knownFiles[match.PathB];
                var pair = left < right ? (left, right) : (right, left);
                if (!pairs.Add(pair))
                {
                    if (!coalesceRepeatedMatches) throw new InvalidDataException("Duplicate analysis result in project.");
                    continue;
                }
                if (coalesceRepeatedMatches) data.DuplicateFiles[retained] = match;
                retained++;
            }
            if (coalesceRepeatedMatches && retained < data.DuplicateFiles.Count)
                data.DuplicateFiles.RemoveRange(retained, data.DuplicateFiles.Count - retained);
            return knownFiles;
        }
    }
}
