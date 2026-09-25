using System.Text.Json;
using System.Text.Json.Nodes;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Services;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

/// <summary>
/// End-to-end workflow integration tests that validate complete user scenarios.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ProjectWorkflowTests
{
    private string _directory = null!;
    private string _left = null!;
    private string _right = null!;
    private string _project = null!;

    [TestInitialize]
    public void CreateArchive()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ClutterFlockIntegration", Guid.NewGuid().ToString("N"));
        _left = Path.Combine(_directory, "photos");
        _right = Path.Combine(_directory, "photos-old");
        _project = Path.Combine(_directory, "snapshot.cfp");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
        File.WriteAllText(Path.Combine(_left, "shared.txt"), "same content");
        File.WriteAllText(Path.Combine(_right, "shared.txt"), "same content");
        File.WriteAllText(Path.Combine(_left, "left.txt"), "only on left");
        File.WriteAllText(Path.Combine(_right, "right.txt"), "only on right");
    }

    [TestCleanup]
    public void RemoveArchive()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockIntegration")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(parent, Path.GetFullPath(_directory));
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private async Task<MainViewModel> AnalyzeAsync()
    {
        var model = new MainViewModel { MinimumSizeMB = 0, MinimumSimilarity = 0 };
        Assert.IsTrue(await model.AddFolderAsync(_left), model.StatusMessage);
        Assert.IsTrue(await model.AddFolderAsync(_right), model.StatusMessage);
        Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
        Assert.HasCount(1, model.FilteredFolderMatches);
        await model.SelectFolderMatchAsync(model.FilteredFolderMatches[0]);
        return model;
    }

    [TestMethod]
    public async Task MultipleRoots_SaveRestartRestore_RetainsResultsFiltersSelectionAndMetadata()
    {
        using var original = await AnalyzeAsync();
        original.ShowUniqueFiles = true;
        original.MinimumSimilarity = 25;
        await original.ApplyFiltersAsync();
        Assert.HasCount(3, original.FileDetails);
        Assert.AreEqual(100.0 / 3, original.FilteredFolderMatches[0].SimilarityPercentage, 0.001);
        Assert.IsTrue(await original.SaveProjectAsync(_project), original.StatusMessage);

        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(_project), restored.StatusMessage);
        CollectionAssert.AreEqual(original.ScanFolders.ToList(), restored.ScanFolders.ToList());
        Assert.AreEqual(25, restored.MinimumSimilarity);
        Assert.AreEqual(0, restored.MinimumSizeMB);
        Assert.IsTrue(restored.ShowUniqueFiles);
        Assert.IsNotNull(restored.SelectedFolderMatch);
        Assert.HasCount(3, restored.FileDetails);
        Assert.HasCount(1, restored.FileDetails.Where(f => f.IsDuplicate).ToList());
        Assert.IsTrue(restored.FileDetails.All(f => !f.HasLeftFile || f.LeftSizeDisplay != "N/A"));
        Assert.IsFalse(restored.OperationInProgress);
        Assert.IsFalse(restored.IsPopulatingResults);
    }

    [TestMethod]
    public async Task DisconnectedRoots_RestoreAndResaveSnapshot_ThenReconnectAndCompare()
    {
        using var original = await AnalyzeAsync();
        original.ShowUniqueFiles = true;
        Assert.IsTrue(await original.SaveProjectAsync(_project));
        Directory.Move(_left, _left + "-offline");
        Directory.Move(_right, _right + "-offline");
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(_project), restored.StatusMessage);
        Assert.HasCount(2, restored.ScanFolders);
        Assert.HasCount(3, restored.FileDetails);
        StringAssert.Contains(restored.StatusMessage, "saved snapshot");
        Assert.IsTrue(await restored.SaveProjectAsync(_project));
        Assert.IsFalse(await restored.RunComparisonAsync());
        Assert.HasCount(1, restored.FilteredFolderMatches);
        Directory.Move(_left + "-offline", _left);
        Directory.Move(_right + "-offline", _right);
        Assert.IsTrue(await restored.RunComparisonAsync(), restored.StatusMessage);
    }

    [TestMethod]
    public async Task ChangedContentWithSameSizeAndTimestamp_IsRehashedAfterRestore()
    {
        using var model = await AnalyzeAsync();
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        var file = Path.Combine(_right, "shared.txt");
        var timestamp = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(file, "fake content");
        File.SetLastWriteTimeUtc(file, timestamp);
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(_project));
        Assert.HasCount(1, restored.FilteredFolderMatches);
        Assert.IsTrue(await restored.RunComparisonAsync(), restored.StatusMessage);
        Assert.IsEmpty(restored.FilteredFolderMatches);
        Assert.IsNull(restored.SelectedFolderMatch);
        Assert.IsEmpty(restored.FileDetails);
    }

    [TestMethod]
    public async Task AddedAndDeletedFiles_AreReflectedByAnotherComparison()
    {
        using var model = await AnalyzeAsync();
        File.Delete(Path.Combine(_left, "shared.txt"));
        File.WriteAllText(Path.Combine(_left, "new.txt"), "new match");
        File.WriteAllText(Path.Combine(_right, "new.txt"), "new match");
        Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
        var match = model.FilteredFolderMatches.Single();
        Assert.AreEqual("new.txt", Path.GetFileName(match.DuplicateFiles.Single().PathA));
        Assert.AreEqual(25.0, match.SimilarityPercentage);
    }

    [TestMethod]
    public async Task OverlappingRoots_RemovingParentPreservesChild_AndSimilarNamedSibling()
    {
        using var model = new MainViewModel { MinimumSizeMB = 0, MinimumSimilarity = 0 };
        Assert.IsTrue(await model.AddFolderAsync(_directory));
        Assert.IsTrue(await model.AddFolderAsync(_left));
        Assert.IsTrue(await model.AddFolderAsync(_right));
        Assert.IsFalse(await model.AddFolderAsync(_left.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.IsTrue(await model.RunComparisonAsync());
        Assert.HasCount(1, model.FilteredFolderMatches);
        model.RemoveFolder(_directory);
        Assert.HasCount(2, model.ScanFolders);
        Assert.IsTrue(await model.SaveProjectAsync(_project), model.StatusMessage);
        var data = await new ProjectManager().LoadProjectAsync(_project);
        Assert.HasCount(2, data.FolderInfoCache);
        model.RemoveFolder(_left);
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        data = await new ProjectManager().LoadProjectAsync(_project);
        CollectionAssert.AreEqual(new[] { _right }, data.FolderInfoCache.Keys.ToArray());
        Assert.IsTrue(data.FileMetadataCache.Keys.All(p => p.StartsWith(_right + Path.DirectorySeparatorChar)));
        Assert.IsTrue(await model.RunComparisonAsync());
        Assert.IsEmpty(model.FilteredFolderMatches);
    }

    [TestMethod]
    public async Task ThreeRoots_ProducesEachFolderPairExactlyOnce()
    {
        using var model = await AnalyzeAsync();
        var third = Path.Combine(_directory, "third");
        Directory.CreateDirectory(third);
        File.Copy(Path.Combine(_left, "shared.txt"), Path.Combine(third, "shared.txt"));
        Assert.IsTrue(await model.AddFolderAsync(third));
        Assert.IsTrue(await model.RunComparisonAsync());
        Assert.HasCount(3, model.FilteredFolderMatches);
        Assert.IsTrue(model.FilteredFolderMatches.All(m => m.SimilarityPercentage <= 100));
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(_project));
        Assert.HasCount(3, restored.FilteredFolderMatches);
    }

    [TestMethod]
    public async Task LegacyProject_RestoresSavedMatchesOffline_AndUpgradesOnSave()
    {
        using var model = await AnalyzeAsync();
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        var legacyData = await new ProjectManager().LoadProjectAsync(_project);
        legacyData.Version = "2.0";
        var json = JsonSerializer.SerializeToNode(legacyData,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
        json["version"] = "1.0";
        foreach (var key in new[] { "fileMetadataCache", "duplicateFiles", "hasAnalysis", "filters", "showUniqueFiles", "selectedLeftFolder", "selectedRightFolder", "applicationName" }) json.Remove(key);
        var legacy = Path.Combine(_directory, "legacy.dfp");
        await File.WriteAllTextAsync(legacy, json.ToJsonString());
        Directory.Move(_left, _left + "-offline");
        Directory.Move(_right, _right + "-offline");
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(legacy), restored.StatusMessage);
        restored.MinimumSimilarity = 0;
        restored.MinimumSizeMB = 0;
        await restored.ApplyFiltersAsync();
        Assert.HasCount(1, restored.FilteredFolderMatches);
        await restored.SelectFolderMatchAsync(restored.FilteredFolderMatches[0]);
        Assert.AreEqual("N/A", restored.FileDetails[0].LeftSizeDisplay);
        Assert.IsTrue(await restored.SaveProjectAsync(_project));
        var upgraded = await new ProjectManager().LoadProjectAsync(_project);
        Assert.AreEqual("3.0", upgraded.Version);
        Assert.HasCount(1, upgraded.DuplicateFiles);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"scanFolders\":[],\"version\":\"2.0\"}")]
    [DataRow("null")]
    [DataRow("{broken")]
    [DataRow("{\"scanFolders\":null}")]
    [DataRow("{\"scanFolders\":[],\"version\":\"99.0\"}")]
    [DataRow("{\"scanFolders\":[],\"applicationName\":\"Another app\"}")]
    [DataRow("{\"scanFolders\":[\"relative-path\"]}")]
    [DataRow("{\"scanFolders\":[],\"filters\":null}")]
    public async Task InvalidProject_DoesNotReplaceActiveSession(string invalidJson)
    {
        using var model = await AnalyzeAsync();
        var selected = model.SelectedFolderMatch;
        await File.WriteAllTextAsync(_project, invalidJson);
        Assert.IsFalse(await model.LoadProjectAsync(_project));
        Assert.AreSame(selected, model.SelectedFolderMatch);
        Assert.HasCount(2, model.ScanFolders);
        Assert.HasCount(1, model.FilteredFolderMatches);
        IProjectManager manager = new ProjectManager();
        Assert.IsFalse(manager.IsValidProjectFile(_project));
    }

    [TestMethod]
    [DataRow("metadata")]
    [DataRow("hash")]
    [DataRow("inconsistent-hash")]
    [DataRow("files")]
    [DataRow("folder")]
    [DataRow("duplicate-result")]
    [DataRow("unknown-result")]
    [DataRow("filter")]
    public async Task CorruptSnapshot_RejectsInconsistentDataWithoutChangingActiveProject(string damage)
    {
        using var model = await AnalyzeAsync();
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        var legacyData = await new ProjectManager().LoadProjectAsync(_project);
        legacyData.Version = "2.0";
        var json = JsonSerializer.SerializeToNode(legacyData,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
        switch (damage)
        {
            case "metadata": json["fileMetadataCache"]!.AsObject().First().Value!["size"] = -1; break;
            case "inconsistent-hash": json["fileHashCache"]!.AsObject()[Path.Combine(_left, "shared.txt")] = new string('0', 64); break;
            case "hash": json["fileHashCache"]!.AsObject()[Path.Combine(_left, "shared.txt")] = "bad hash"; break;
            case "files": json["folderInfoCache"]!.AsObject()[_left]!["files"] = null; break;
            case "folder": json["folderInfoCache"]!.AsObject()[_left]!["files"]![0] = Path.Combine(_right, "shared.txt"); break;
            case "duplicate-result": json["duplicateFiles"]!.AsArray().Add(json["duplicateFiles"]![0]!.DeepClone()); break;
            case "unknown-result": json["duplicateFiles"]![0]!["pathA"] = Path.Combine(_left, "missing.txt"); break;
            case "filter": json["filters"]!["minimumSimilarityPercent"] = 101; break;
        }
        await File.WriteAllTextAsync(_project, json.ToJsonString());
        var before = model.SelectedFolderMatch;
        Assert.IsFalse(await model.LoadProjectAsync(_project));
        Assert.AreSame(before, model.SelectedFolderMatch);
        Assert.HasCount(1, model.FilteredFolderMatches);
    }

    [TestMethod]
    public async Task MissingRootAndMissingProject_ReportFailure_ThenRecover()
    {
        using var model = new MainViewModel();
        Assert.IsFalse(await model.AddFolderAsync(Path.Combine(_directory, "missing")));
        Assert.IsEmpty(model.ScanFolders);
        Assert.IsFalse(await model.LoadProjectAsync(_project));
        Assert.IsTrue(await model.AddFolderAsync(_left));
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        Assert.IsTrue(new ProjectManager().IsValidProjectFile(_project));
        Assert.IsFalse(await model.SaveProjectAsync(Path.Combine(_directory, "missing", "project.cfp")));
        Assert.IsTrue(new ProjectManager().IsValidProjectFile(_project));
    }

    [TestMethod]
    public async Task CancelAddAndLoad_DoNotPartiallyReplaceRoots()
    {
        using var model = await AnalyzeAsync();
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        var adding = model.AddFolderAsync(_directory);
        model.CancelOperation();
        Assert.IsFalse(await adding);
        Assert.HasCount(2, model.ScanFolders);
        var loading = model.LoadProjectAsync(_project);
        model.CancelOperation();
        Assert.IsFalse(await loading);
        Assert.HasCount(2, model.ScanFolders);
        Assert.HasCount(1, model.FilteredFolderMatches);
        Assert.IsTrue(await model.LoadProjectAsync(_project));
    }

    [TestMethod]
    public async Task FailedOverwrite_PreservesPreviousProject_AndCleansTemporaryFile()
    {
        using var model = await AnalyzeAsync();
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        var original = await File.ReadAllBytesAsync(_project);
        using (var locked = new FileStream(_project, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            model.MinimumSimilarity = 99;
            Assert.IsFalse(await model.SaveProjectAsync(_project));
        }
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(_project));
        Assert.IsEmpty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        Assert.AreEqual(99, (await new ProjectManager().LoadProjectAsync(_project)).Filters.MinimumSimilarityPercent);
    }

    [TestMethod]
    public async Task RepeatedSaveRestore_PreservesCreationDateAndSnapshot()
    {
        using var initial = await AnalyzeAsync();
        Assert.IsTrue(await initial.SaveProjectAsync(_project));
        var created = (await new ProjectManager().LoadProjectAsync(_project)).CreatedDate;
        for (var i = 0; i < 3; i++)
        {
            using var restored = new MainViewModel();
            Assert.IsTrue(await restored.LoadProjectAsync(_project));
            Assert.IsTrue(await restored.SaveProjectAsync(_project));
            var data = await new ProjectManager().LoadProjectAsync(_project);
            Assert.AreEqual(created, data.CreatedDate);
            Assert.HasCount(1, data.DuplicateFiles);
            Assert.HasCount(4, data.FileMetadataCache);
        }
    }

    [TestMethod]
    public async Task CancelComparison_RetainsResults_AndDisallowsOverlappingOperations()
    {
        using var model = await AnalyzeAsync();
        var previous = model.FilteredFolderMatches[0];
        var running = model.RunComparisonAsync();
        Assert.IsFalse(await model.LoadProjectAsync(_project));
        Assert.IsFalse(await model.SaveProjectAsync(_project));
        Assert.IsFalse(await model.AddFolderAsync(_directory));
        model.CancelOperation();
        Assert.IsFalse(await running);
        Assert.AreSame(previous, model.FilteredFolderMatches[0]);
        Assert.IsTrue(model.CanRunComparison);
        Assert.IsTrue(await model.RunComparisonAsync());
    }

    [TestMethod]
    public async Task RapidSelections_OnlyLatestSelectionPopulatesDetails()
    {
        using var model = await AnalyzeAsync();
        var pending = model.SelectFolderMatchAsync(model.FilteredFolderMatches[0]);
        await model.SelectFolderMatchAsync(null);
        await pending;
        Assert.IsEmpty(model.FileDetails);
        Assert.AreEqual("", model.LeftFolderDisplay);
        Assert.IsNull(model.SelectedFolderMatch);
    }

    [TestMethod]
    public async Task FilterCompletion_IsAwaitable_AndInvalidFiltersPreserveResults()
    {
        using var model = await AnalyzeAsync();
        model.MinimumSimilarity = 90;
        await model.ApplyFiltersAsync();
        Assert.IsEmpty(model.FilteredFolderMatches);
        Assert.IsEmpty(model.FileDetails);
        model.MinimumSimilarity = 0;
        await model.ApplyFiltersAsync();
        Assert.HasCount(1, model.FilteredFolderMatches);
        model.MinimumSimilarity = double.NaN;
        await model.ApplyFiltersAsync();
        Assert.HasCount(1, model.FilteredFolderMatches);
        var previous = model.FilteredFolderMatches[0];
        Assert.IsFalse(await model.RunComparisonAsync());
        Assert.AreSame(previous, model.FilteredFolderMatches[0]);
        Assert.IsFalse(await model.SaveProjectAsync(_project));
        Assert.IsFalse(File.Exists(_project));
    }

    [TestMethod]
    public async Task ScannedButUncomparedProject_RestoresRoots_ThenCanBeCompared()
    {
        using var model = new MainViewModel { MinimumSizeMB = 0, MinimumSimilarity = 0 };
        Assert.IsTrue(await model.AddFolderAsync(_directory));
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(_project));
        Assert.IsEmpty(restored.FilteredFolderMatches);
        Assert.IsFalse(restored.CanApplyFilters);
        Assert.IsTrue(await restored.RunComparisonAsync());
        Assert.HasCount(1, restored.FilteredFolderMatches);
    }

    [TestMethod]
    public async Task UnicodeRootsAndNames_RoundTripWithoutDataLoss()
    {
        var unicode = Path.Combine(_directory, "写真 été");
        Directory.CreateDirectory(unicode);
        File.WriteAllText(Path.Combine(unicode, "写真.txt"), "写真");
        File.WriteAllText(Path.Combine(_left, "写真.txt"), "写真");
        using var model = new MainViewModel { MinimumSizeMB = 0, MinimumSimilarity = 0 };
        Assert.IsTrue(await model.AddFolderAsync(unicode));
        Assert.IsTrue(await model.AddFolderAsync(_left));
        Assert.IsTrue(await model.RunComparisonAsync());
        Assert.IsTrue(await model.SaveProjectAsync(_project));
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(_project));
        Assert.IsTrue(restored.ScanFolders.Contains(unicode));
        Assert.AreEqual("写真.txt", Path.GetFileName(restored.FilteredFolderMatches[0].DuplicateFiles[0].PathA));
    }
}
