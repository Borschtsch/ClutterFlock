using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Services;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public sealed class FolderContentQualificationTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "ClutterFlockContent", Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockContent")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, true);
    }

    private void Write(string folder, string name, string content)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        File.WriteAllText(Path.Combine(directory, name), content);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EmptyOverlapAlone_DoesNotQualify_EvenWithOtherNonEmptyFiles(bool otherContent)
    {
        Write("a", "empty", ""); Write("b", "empty", "");
        if (otherContent)
        {
            Write("a", "different", "AAAA"); Write("b", "different", "BBBB");
            Write("a", "only-a", "unique"); Write("b", "only-b", "unique");
        }
        ICacheManager cache = new CacheManager();
        var errors = new ErrorRecoveryService();
        var folders = await new FolderScanner(cache, errors).ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        IDuplicateAnalyzer analyzer = new DuplicateAnalyzer(cache, errors);
        var files = await analyzer.FindDuplicateFilesAsync(folders, null, CancellationToken.None);
        Assert.HasCount(1, files, "The shared empty file must remain file-level evidence.");
        Assert.IsEmpty(await analyzer.AggregateFolderMatchesAsync(files, cache));

        using var model = new MainViewModel { MinimumSimilarity = 0, MinimumSizeMB = 0 };
        Assert.IsTrue(await model.AddFolderAsync(_root));
        var visiblePairs = 0;
        model.FilteredFolderMatches.CollectionChanged += (_, args) => visiblePairs += args.NewItems?.Count ?? 0;
        Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
        Assert.AreEqual(0, visiblePairs, "Empty-only pairs must never appear, even provisionally.");
        Assert.IsEmpty(model.FilteredFolderMatches);
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task LiveQualifiedPair_RetainsEmptyEvidence_AndRoundTripsOffline()
    {
        foreach (var folder in new[] { "a", "b", "c" })
            for (var i = 0; i < 12; i++) Write(folder, $"empty-{i}", "");
        Write("a", "shared", "actual content"); Write("b", "shared", "actual content");
        Write("c", "shared", "other contents");
        using var model = new MainViewModel { MinimumSimilarity = 0, MinimumSizeMB = 0 };
        Assert.IsTrue(await model.AddFolderAsync(_root));
        var previews = new List<FolderMatch>();
        void OnRows(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!model.IsLivePreview || args.NewItems == null) return;
            foreach (FolderMatch pair in args.NewItems)
            {
                Assert.IsTrue(pair.DuplicateFiles.Any(f => Path.GetFileName(f.PathA) == "shared"),
                    "A pair cannot appear before non-empty content is verified.");
                previews.Add(pair);
            }
        }
        model.FilteredFolderMatches.CollectionChanged += OnRows;
        Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
        model.FilteredFolderMatches.CollectionChanged -= OnRows;
        Assert.IsNotEmpty(previews);
        var pair = model.FilteredFolderMatches.Single();
        Assert.AreEqual(Path.Combine(_root, "a"), pair.LeftFolder);
        Assert.AreEqual(Path.Combine(_root, "b"), pair.RightFolder);
        Assert.HasCount(13, pair.DuplicateFiles);
        Assert.AreEqual(100, pair.SimilarityPercentage);
        await model.SelectFolderMatchAsync(pair);
        Assert.HasCount(13, model.FileDetails.Where(f => f.IsDuplicate).ToList());

        var project = Path.Combine(_root, "snapshot.cfp");
        Assert.IsTrue(await model.SaveProjectAsync(project));
        // Moving these known fixture directories makes the saved paths unavailable.
        foreach (var folder in new[] { "a", "b", "c" })
            Directory.Move(Path.Combine(_root, folder), Path.Combine(_root, folder + "-offline"));
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(project), restored.StatusMessage);
        Assert.HasCount(13, restored.FilteredFolderMatches.Single().DuplicateFiles);
        Assert.AreEqual(100, restored.FilteredFolderMatches.Single().SimilarityPercentage);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OldSnapshot_QualifiesEachPairOffline_WithOrWithoutMetadata(bool legacy)
    {
        foreach (var folder in new[] { "a", "b", "c" }) Write(folder, "empty", "");
        Write("a", "shared", "content"); Write("b", "shared", "content");
        Write("c", "shared", "changed");
        ICacheManager cache = new CacheManager();
        var errors = new ErrorRecoveryService();
        var folders = await new FolderScanner(cache, errors).ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        IDuplicateAnalyzer analyzer = new DuplicateAnalyzer(cache, errors);
        var files = await analyzer.FindDuplicateFilesAsync(folders, null, CancellationToken.None);
        Assert.HasCount(4, files);
        var data = cache.ExportToProjectData(new() { _root });
        data.DuplicateFiles = files; data.HasAnalysis = true;
        data.Filters.MinimumSimilarityPercent = 0; data.Filters.MinimumSizeBytes = 0;
        var project = Path.Combine(_root, "old.cfp");
        IProjectManager projects = new ProjectManager();
        if (legacy)
        {
            var json = JsonSerializer.SerializeToNode(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!.AsObject();
            json["version"] = "1.0";
            foreach (var key in new[] { "fileMetadataCache", "duplicateFiles", "filters", "hasAnalysis" }) json.Remove(key);
            await File.WriteAllTextAsync(project, json.ToJsonString());
        }
        else await projects.SaveProjectAsync(project, data);
        foreach (var folder in new[] { "a", "b", "c" })
            Directory.Move(Path.Combine(_root, folder), Path.Combine(_root, folder + "-offline"));
        using var model = new MainViewModel();
        Assert.IsTrue(await model.LoadProjectAsync(project), model.StatusMessage);
        // Version 1 projects restore their historical default size/similarity filters.
        model.MinimumSimilarity = 0; model.MinimumSizeMB = 0;
        await model.ApplyFiltersAsync();
        var pair = model.FilteredFolderMatches.Single();
        Assert.AreEqual(Path.Combine(_root, "a"), pair.LeftFolder);
        Assert.AreEqual(Path.Combine(_root, "b"), pair.RightFolder);
        Assert.HasCount(2, pair.DuplicateFiles);
        Assert.AreEqual(100, pair.SimilarityPercentage);
        Assert.IsTrue(await model.SaveProjectAsync(project));
        Assert.HasCount(2, (await projects.LoadProjectAsync(project)).DuplicateFiles);
    }
}
