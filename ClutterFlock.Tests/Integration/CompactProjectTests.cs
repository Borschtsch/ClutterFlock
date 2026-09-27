using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Services;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

/// <summary>
/// Exercises compact persistence through real archive scans, migration, and offline restore.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class CompactProjectTests
{
    private string _root = null!;
    private string _project = null!;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ClutterFlockCompact", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "compact.cfp");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockCompact")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, recursive: true);
    }

    private async Task<MainViewModel> CreateAnalysisAsync(int folders = 2, int files = 2)
    {
        var archiveRoot = Path.Combine(_root, "Backups spanning decades", "Unsorted original photography collection", "Archives copied between old computers");
        Directory.CreateDirectory(archiveRoot);
        for (var folder = 0; folder < folders; folder++)
        {
            var directory = Directory.CreateDirectory(Path.Combine(archiveRoot, $"backup-{folder:D3}")).FullName;
            for (var file = 0; file < files; file++)
                await File.WriteAllTextAsync(Path.Combine(directory, $"image-{file:D4}.txt"), $"Original image content {file}");
        }
        Directory.CreateDirectory(Path.Combine(archiveRoot, "empty"));
        var model = new MainViewModel { MinimumSizeMB = 0, MinimumSimilarity = 0, ShowUniqueFiles = true };
        Assert.IsTrue(await model.AddFolderAsync(archiveRoot), model.StatusMessage);
        Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
        await model.SelectFolderMatchAsync(model.FilteredFolderMatches.First());
        Assert.IsTrue(await model.SaveProjectAsync(_project), model.StatusMessage);
        return model;
    }

    [TestMethod]
    public async Task RepeatedBackups_SaveIsAtLeast90PercentSmaller_AndRestoresIdenticalResults()
    {
        using var original = await CreateAnalysisAsync(folders: 40, files: 25);
        IProjectManager manager = new ProjectManager();
        var snapshot = await manager.LoadProjectAsync(_project);
        snapshot.Version = "2.0";
        var legacy = Path.Combine(_root, "legacy.cfp");
        await using (var stream = File.Create(legacy))
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
        var previousBytes = new FileInfo(legacy).Length;
        var compactBytes = new FileInfo(_project).Length;
        TestContext.WriteLine($"SIZE: {previousBytes:N0} bytes legacy -> {compactBytes:N0} bytes compact ({100.0 * (1 - compactBytes / (double)previousBytes):F2}% smaller). Files: {snapshot.FileMetadataCache.Count}; matches: {snapshot.DuplicateFiles.Count}.");
        Assert.IsLessThan(previousBytes / 10, compactBytes, "The compact archive should remove the repeated path overhead.");

        // Simulate disconnected source drives; loading must use only persisted data.
        Directory.Move(original.ScanFolders.Single(), original.ScanFolders.Single() + "-offline");
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(_project), restored.StatusMessage);
        Assert.AreEqual(original.FilteredFolderMatches.Count, restored.FilteredFolderMatches.Count);
        Assert.AreEqual(780, restored.FilteredFolderMatches.Count);
        Assert.HasCount(25, restored.FileDetails);
        Assert.AreEqual(original.SelectedFolderMatch!.LeftFolder, restored.SelectedFolderMatch!.LeftFolder);
        Assert.IsTrue(restored.FilteredFolderMatches.All(m => m.SimilarityPercentage == 100));

        // Real version 2 JSON still loads and resaves to the compact representation.
        var migrated = Path.Combine(_root, "migrated.cfp");
        Assert.IsTrue(await restored.LoadProjectAsync(legacy), restored.StatusMessage);
        Assert.IsTrue(await restored.SaveProjectAsync(migrated), restored.StatusMessage);
        Assert.IsLessThan(previousBytes / 10, new FileInfo(migrated).Length);
        var migratedSnapshot = await manager.LoadProjectAsync(migrated);
        Assert.AreEqual("3.0", migratedSnapshot.Version);
        CollectionAssert.AreEqual(snapshot.ScanFolders, migratedSnapshot.ScanFolders);
        CollectionAssert.AreEquivalent(snapshot.DuplicateFiles, migratedSnapshot.DuplicateFiles);
        Assert.AreEqual(snapshot.CreatedDate, migratedSnapshot.CreatedDate);
        Assert.AreEqual(snapshot.FileMetadataCache.Count, migratedSnapshot.FileMetadataCache.Count);
        foreach (var entry in snapshot.FileMetadataCache)
        {
            var actual = migratedSnapshot.FileMetadataCache[entry.Key];
            Assert.AreEqual(entry.Value.Size, actual.Size);
            Assert.AreEqual(entry.Value.LastWriteTime, actual.LastWriteTime);
            Assert.AreEqual(entry.Value.FileName, actual.FileName);
        }
        foreach (var entry in snapshot.FileHashCache)
            Assert.AreEqual(entry.Value, migratedSnapshot.FileHashCache[entry.Key]);
    }

    [TestMethod]
    public async Task StoredFiles_AreGroupedByFolder_AndMatchesContainOnlyNumericReferences()
    {
        using var model = await CreateAnalysisAsync();
        using var archive = ZipFile.OpenRead(_project);
        CollectionAssert.AreEquivalent(new[] { "project.json", "folders.json", "matches.bin" }, archive.Entries.Select(e => e.FullName).ToArray());
        using var input = archive.GetEntry("folders.json")!.Open();
        using var json = await JsonDocument.ParseAsync(input);
        var entries = json.RootElement.EnumerateArray().ToList();
        Assert.HasCount(4, entries); // archive root, two backups, and an empty folder
        var fileCount = 0;
        foreach (var folder in entries)
        {
            Assert.IsFalse(Path.IsPathRooted(folder.GetProperty("path").GetString()!));
            Assert.AreEqual(0, folder.GetProperty("root").GetInt32());
            foreach (var file in folder.GetProperty("files").EnumerateArray())
            {
                var name = file.GetProperty("name").GetString()!;
                Assert.AreEqual(Path.GetFileName(name), name);
                Assert.IsFalse(name.Contains('\\') || name.Contains('/'));
                Assert.IsFalse(file.TryGetProperty("fileName", out _));
                fileCount++;
            }
        }
        Assert.AreEqual(4, fileCount);
        Assert.AreEqual(2 * 8, archive.GetEntry("matches.bin")!.Length);
    }

    [TestMethod]
    [DataRow("missing-entry")]
    [DataRow("missing-header-field")]
    [DataRow("unknown-version")]
    [DataRow("null-roots")]
    [DataRow("invalid-root")]
    [DataRow("absolute-folder")]
    [DataRow("escaping-folder")]
    [DataRow("null-files")]
    [DataRow("duplicate-folder")]
    [DataRow("escaping-file")]
    [DataRow("duplicate-file")]
    [DataRow("partial-metadata")]
    [DataRow("unknown-file-reference")]
    [DataRow("truncated-matches")]
    public async Task DamagedCompactProject_DoesNotReplaceCurrentSession(string damage)
    {
        using var model = await CreateAnalysisAsync();
        var selected = model.SelectedFolderMatch;
        using (var archive = ZipFile.Open(_project, ZipArchiveMode.Update))
        {
            if (damage == "missing-entry") archive.GetEntry("folders.json")!.Delete();
            else if (damage is "unknown-file-reference" or "truncated-matches")
            {
                archive.GetEntry("matches.bin")!.Delete();
                using var output = archive.CreateEntry("matches.bin").Open();
                var invalid = Enumerable.Repeat((byte)255, damage == "truncated-matches" ? 1 : 16).ToArray();
                output.Write(invalid);
            }
            else
            {
                var headerDamage = damage is "missing-header-field" or "unknown-version" or "null-roots";
                var name = headerDamage ? "project.json" : "folders.json";
                var entry = archive.GetEntry(name)!;
                JsonNode json;
                using (var input = entry.Open()) json = JsonNode.Parse(input)!;
                if (headerDamage)
                {
                    if (damage == "missing-header-field") json.AsObject().Remove("matchCount");
                    if (damage == "unknown-version") json["version"] = "99.0";
                    if (damage == "null-roots") json["scanFolders"] = null;
                }
                else
                {
                    var folder = json.AsArray().First(f => f!["files"]!.AsArray().Count > 0)!;
                    var files = folder["files"]!.AsArray();
                    switch (damage)
                    {
                        case "invalid-root": folder["root"] = 99; break;
                        case "absolute-folder": folder["path"] = _root; break;
                        case "escaping-folder": folder["path"] = "..\\outside"; break;
                        case "null-files": folder["files"] = null; break;
                        case "duplicate-folder":
                            var conflicting = folder.DeepClone();
                            conflicting["totalSize"] = folder["totalSize"]!.GetValue<long>() + 1;
                            json.AsArray().Add(conflicting); break;
                        case "escaping-file": files[0]!["name"] = "..\\outside.txt"; break;
                        case "duplicate-file": files.Add(files[0]!.DeepClone()); break;
                        case "partial-metadata": files[0]!.AsObject().Remove("size"); break;
                    }
                }
                entry.Delete();
                using var output = archive.CreateEntry(name).Open();
                using var writer = new StreamWriter(output, new UTF8Encoding(false));
                writer.Write(json.ToJsonString());
            }
        }
        Assert.IsFalse(await model.LoadProjectAsync(_project));
        Assert.AreSame(selected, model.SelectedFolderMatch);
        Assert.HasCount(1, model.FilteredFolderMatches);
        IProjectManager manager = new ProjectManager();
        Assert.IsFalse(manager.IsValidProjectFile(_project));
    }

    [TestMethod]
    [DataRow("same-path")]
    [DataRow("empty-folder")]
    [DataRow("case-alias")]
    [DataRow("overlapping-root")]
    public async Task RepeatedFolderRecords_RestoreOfflinePreserveFileIds_AndResaveOnce(string alias)
    {
        using var original = await CreateAnalysisAsync(folders: 3, files: 2);
        var manager = new ProjectManager();
        var before = await manager.LoadProjectAsync(_project);
        AddRepeatedFolder(alias);
        var savedBytes = await File.ReadAllBytesAsync(_project);
        Directory.Move(original.ScanFolders.Single(), original.ScanFolders.Single() + "-offline");
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(_project), restored.StatusMessage);
        Assert.HasCount(3, restored.FilteredFolderMatches);
        Assert.IsTrue(restored.FilteredFolderMatches.All(m => m.DuplicateFiles.Count == 2 && m.SimilarityPercentage == 100));
        Assert.AreEqual(before.SelectedLeftFolder, restored.SelectedFolderMatch!.LeftFolder);
        Assert.AreEqual(before.SelectedRightFolder, restored.SelectedFolderMatch.RightFolder);
        CollectionAssert.AreEqual(savedBytes, await File.ReadAllBytesAsync(_project), "Loading must not rewrite the original save.");
        var migrated = Path.Combine(_root, "coalesced.cfp");
        Assert.IsTrue(await restored.SaveProjectAsync(migrated), restored.StatusMessage);
        var after = await manager.LoadProjectAsync(migrated);
        CollectionAssert.AreEquivalent(before.DuplicateFiles, after.DuplicateFiles);
        Assert.AreEqual(before.FolderInfoCache.Count, after.FolderInfoCache.Count);
        Assert.AreEqual(before.FileMetadataCache.Count, after.FileMetadataCache.Count);
        foreach (var file in before.FileMetadataCache)
        {
            Assert.AreEqual(file.Value.Size, after.FileMetadataCache[file.Key].Size);
            Assert.AreEqual(file.Value.LastWriteTime, after.FileMetadataCache[file.Key].LastWriteTime);
            Assert.AreEqual(before.FileHashCache[file.Key], after.FileHashCache[file.Key]);
        }
    }

    [TestMethod]
    [DataRow("size")]
    [DataRow("timestamp")]
    [DataRow("hash")]
    [DataRow("file-list")]
    [DataRow("folder-date")]
    public async Task RepeatedFolderWithConflictingEvidence_ReportsPathAndRetainsSession(string conflict)
    {
        using var model = await CreateAnalysisAsync();
        var selected = model.SelectedFolderMatch;
        var path = AddRepeatedFolder("same-path", conflict);
        var savedBytes = await File.ReadAllBytesAsync(_project);
        Assert.IsFalse(await model.LoadProjectAsync(_project));
        StringAssert.Contains(model.StatusMessage, "Conflicting stored");
        StringAssert.Contains(model.StatusMessage, path);
        Assert.AreSame(selected, model.SelectedFolderMatch);
        Assert.HasCount(1, model.FilteredFolderMatches);
        CollectionAssert.AreEqual(savedBytes, await File.ReadAllBytesAsync(_project));
    }

    // Rewrite an actual saved container, preserving its ordinal file-ID format.
    private string AddRepeatedFolder(string alias, string? conflict = null)
    {
        using var archive = ZipFile.Open(_project, ZipArchiveMode.Update);
        JsonNode Read(string name)
        {
            using var stream = archive.GetEntry(name)!.Open();
            return JsonNode.Parse(stream)!;
        }
        void Write(string name, JsonNode node)
        {
            archive.GetEntry(name)!.Delete();
            using var stream = archive.CreateEntry(name).Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(node.ToJsonString());
        }
        var header = Read("project.json");
        var folders = Read("folders.json").AsArray();
        var folder = folders.First(f => alias == "empty-folder"
            ? f!["files"]!.AsArray().Count == 0 : f!["files"]!.AsArray().Count > 0)!;
        var index = folders.IndexOf(folder);
        var offset = folders.Take(index).Sum(f => f!["files"]!.AsArray().Count);
        var count = folder["files"]!.AsArray().Count;
        var fullPath = Path.GetFullPath(Path.Combine(header["scanFolders"]![folder["root"]!.GetValue<int>()]!.GetValue<string>(), folder["path"]!.GetValue<string>()));
        var copy = folder.DeepClone();
        if (alias == "case-alias") copy["path"] = folder["path"]!.GetValue<string>().ToUpperInvariant();
        if (alias == "overlapping-root")
        {
            copy["root"] = header["scanFolders"]!.AsArray().Count;
            header["scanFolders"]!.AsArray().Add(fullPath);
            copy["path"] = ".";
        }
        var file = copy["files"]!.AsArray().FirstOrDefault()!;
        switch (conflict)
        {
            case "size": file["size"] = file["size"]!.GetValue<long>() + 1; break;
            case "timestamp": file["lastWriteTime"] = DateTime.MinValue; break;
            case "hash": file["hash"] = new string('0', 64); break;
            case "file-list": file["name"] = "different-file.txt"; break;
            case "folder-date": copy["latestModificationDate"] = DateTime.MinValue; break;
        }
        // Insert before later records: loaders must retain the repeated occurrence's ID slots.
        folders.Insert(index + 1, copy);
        byte[] matches;
        using (var input = archive.GetEntry("matches.bin")!.Open())
        using (var output = new MemoryStream()) { input.CopyTo(output); matches = output.ToArray(); }
        for (var i = 0; i < matches.Length; i += 4)
        {
            var id = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(matches.AsSpan(i));
            if (id >= offset + count) id += count;
            else if (id >= offset) id += count; // Reference the duplicate occurrence instead of the original.
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(matches.AsSpan(i), id);
        }
        var extra = matches[..8].ToArray();
        for (var i = 0; i < extra.Length; i += 4)
        {
            var id = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(extra.AsSpan(i));
            if (id >= offset + count && id < offset + 2 * count) id -= count;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(extra.AsSpan(i), id);
        }
        archive.GetEntry("matches.bin")!.Delete();
        using (var output = archive.CreateEntry("matches.bin").Open()) { output.Write(matches); output.Write(extra); }
        header["matchCount"] = header["matchCount"]!.GetValue<int>() + 1;
        Write("project.json", header); Write("folders.json", folders);
        return fullPath;
    }

    [TestMethod]
    public async Task CancelledCompactLoad_LeavesExistingProjectIntact()
    {
        using var model = await CreateAnalysisAsync();
        var selected = model.SelectedFolderMatch;
        using var source = new CancellationTokenSource();
        source.Cancel();
        IProjectManager manager = new ProjectManager();
        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.LoadProjectAsync(_project, source.Token));
        var loading = model.LoadProjectAsync(_project);
        model.CancelOperation();
        Assert.IsFalse(await loading);
        Assert.AreSame(selected, model.SelectedFolderMatch);
    }

}
