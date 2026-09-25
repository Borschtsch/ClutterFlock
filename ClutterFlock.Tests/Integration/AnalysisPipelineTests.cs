using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.Tests.Integration;

/// <summary>
/// Validates component integration using real file-system data and public service interfaces.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AnalysisPipelineTests
{
    private string _root = null!;
    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "ClutterFlockPipeline", Guid.NewGuid().ToString("N")));
    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockPipeline")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, recursive: true);
    }
    private string Write(string folder, string name, string text)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    [TestMethod]
    public async Task RealFiles_NameSizeAndHashPipeline_ExcludesDifferentAndRenamedContent()
    {
        Write("a", "same.txt", "match"); Write("b", "SAME.TXT", "match");
        Write("a", "changed.txt", "aaaa"); Write("b", "changed.txt", "bbbb");
        Write("a", "renamed.txt", "copy"); Write("b", "newname.txt", "copy");
        Write("a", "size.txt", "short"); Write("b", "size.txt", "longer");
        ICacheManager cache = new CacheManager();
        IErrorRecoveryService errors = new ErrorRecoveryService();
        IFolderScanner scanner = new FolderScanner(cache, errors);
        var folders = await scanner.ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        IDuplicateAnalyzer analyzer = new DuplicateAnalyzer(cache, errors);
        var files = await analyzer.FindDuplicateFilesAsync(folders, null, CancellationToken.None);
        Assert.HasCount(1, files);
        var pairs = await analyzer.AggregateFolderMatchesAsync(files, cache);
        Assert.AreEqual(100.0 / 7, pairs.Single().SimilarityPercentage, .001);
        IFileComparer comparer = new FileComparer();
        var details = comparer.BuildFileComparison(pairs[0].LeftFolder, pairs[0].RightFolder, files, cache);
        Assert.HasCount(5, details);
        Assert.HasCount(1, comparer.FilterFileDetails(details, false));
        Assert.HasCount(5, comparer.FilterFileDetails(details, true));
        Assert.IsFalse(errors.GetErrorSummary().HasErrors);
    }

    [TestMethod]
    public async Task OppositeFileOrdering_AggregatesIntoOneFolderPair()
    {
        Write("a", "one", "one"); Write("b", "one", "one");
        Write("a", "two", "two"); Write("b", "two", "two");
        ICacheManager cache = new CacheManager();
        var errors = new ErrorRecoveryService();
        IFolderScanner scanner = new FolderScanner(cache, errors);
        await scanner.ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        var a = Path.Combine(_root, "a"); var b = Path.Combine(_root, "b");
        IDuplicateAnalyzer analyzer = new DuplicateAnalyzer(cache, errors);
        var pairs = await analyzer.AggregateFolderMatchesAsync(new()
        {
            new(Path.Combine(a, "one"), Path.Combine(b, "one")),
            new(Path.Combine(b, "two"), Path.Combine(a, "two"))
        }, cache);
        Assert.HasCount(1, pairs);
        Assert.AreEqual(100, pairs[0].SimilarityPercentage);
    }

    [TestMethod]
    public async Task LockedFile_IsReportedAndSkipped_WhileOtherMatchesRemain()
    {
        var lockedPath = Write("a", "locked", "locked"); Write("b", "locked", "locked");
        Write("a", "good", "good"); Write("b", "good", "good");
        ICacheManager cache = new CacheManager();
        IErrorRecoveryService errors = new ErrorRecoveryService();
        IFolderScanner scanner = new FolderScanner(cache, errors);
        var folders = await scanner.ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        IDuplicateAnalyzer analyzer = new DuplicateAnalyzer(cache, errors);
        var matches = await analyzer.FindDuplicateFilesAsync(folders, null, CancellationToken.None);
        Assert.HasCount(1, matches);
        Assert.AreEqual("good", Path.GetFileName(matches[0].PathA));
        Assert.IsTrue(errors.GetErrorSummary().SkippedPaths.Contains(lockedPath));
    }

    [TestMethod]
    public async Task CancellationDuringHashPhase_PropagatesWithoutReturningPartialMatches()
    {
        Write("a", "same", "same"); Write("b", "same", "same");
        ICacheManager cache = new CacheManager();
        var errors = new ErrorRecoveryService();
        var scanner = new FolderScanner(cache, errors);
        var folders = await scanner.ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var progress = new InlineProgress(p => { if (p.Phase == AnalysisPhase.ComparingFiles) cancel.Cancel(); });
        IDuplicateAnalyzer analyzer = new DuplicateAnalyzer(cache, errors);
        await Assert.ThrowsAsync<OperationCanceledException>(() => analyzer.FindDuplicateFilesAsync(folders, progress, cancel.Token));
    }

    [TestMethod]
    public async Task RemovingOneRoot_DropsUnhashedMetadata_ButPreservesSibling()
    {
        var a = Write("photos", "unique-a", "a");
        var b = Write("photos-old", "unique-b", "b");
        ICacheManager cache = new CacheManager();
        IFolderScanner scanner = new FolderScanner(cache, new ErrorRecoveryService());
        await scanner.ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        cache.RemoveFolderFromCache(Path.GetDirectoryName(a)!);
        Assert.IsNull(cache.GetFileMetadata(a));
        Assert.IsNotNull(cache.GetFileMetadata(b));
        Assert.IsTrue(cache.IsFolderCached(Path.GetDirectoryName(b)!));
    }

    [TestMethod]
    public async Task LargeResultSet_CompleteAnalysisRoundTripPreservesAllPairs()
    {
        const int count = 20;
        for (var i = 0; i < count; i++) Write(i.ToString(), "same", new string('x', 1024));
        using var model = new ClutterFlock.ViewModels.MainViewModel { MinimumSizeMB = 0, MinimumSimilarity = 100 };
        Assert.IsTrue(await model.AddFolderAsync(_root));
        Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
        Assert.HasCount(count * (count - 1) / 2, model.FilteredFolderMatches);
        var project = Path.Combine(_root, "large.cfp");
        Assert.IsTrue(await model.SaveProjectAsync(project));
        using var restored = new ClutterFlock.ViewModels.MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(project));
        Assert.HasCount(190, restored.FilteredFolderMatches);
        Assert.IsFalse(restored.IsPopulatingResults);
    }

    private sealed class InlineProgress(Action<AnalysisProgress> report) : IProgress<AnalysisProgress>
    {
        public void Report(AnalysisProgress value) => report(value);
    }
}
