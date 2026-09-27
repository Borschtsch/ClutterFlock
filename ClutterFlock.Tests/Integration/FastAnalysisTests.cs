using System.Diagnostics;
using System.Collections.Specialized;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Services;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public sealed class FastAnalysisTests
{
    private string _root = null!;
    public TestContext TestContext { get; set; } = null!;
    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "ClutterFlockFast", Guid.NewGuid().ToString("N")));
    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockFast")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, true);
    }
    private string Write(string folder, string name, byte[] data)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, folder)).FullName, name);
        File.WriteAllBytes(path, data); return path;
    }
    private async Task<(ICacheManager Cache, IDuplicateAnalyzer Analyzer, List<string> Folders)> Scan()
    {
        ICacheManager cache = new CacheManager(); var errors = new ErrorRecoveryService();
        var folders = await new FolderScanner(cache, errors).ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        return (cache, new DuplicateAnalyzer(cache, errors), folders);
    }

    [TestMethod]
    public async Task LargeDifferentFiles_SamplingAvoidsFullHashes_AndPreservesDifferentEvidenceOffline()
    {
        var bytes = new byte[8 * 1024 * 1024];
        for (var i = 0; i < 4; i++)
        {
            Array.Fill(bytes, (byte)(i + 1)); Write("a", $"different-{i}.bin", bytes);
            Array.Fill(bytes, (byte)(i + 20)); Write("b", $"different-{i}.bin", bytes);
        }
        Write("a", "shared", [1, 2, 3]); Write("b", "shared", [1, 2, 3]);
        var baseline = await Scan(); var before = new AnalysisOptions { UseContentSampling = false };
        var clock = Stopwatch.StartNew();
        var expected = await baseline.Analyzer.FindDuplicateFilesAsync(baseline.Folders, null, CancellationToken.None, options: before);
        var baselineMs = clock.ElapsedMilliseconds;
        var scan = await Scan(); var options = new AnalysisOptions(); clock.Restart();
        var actual = await scan.Analyzer.FindDuplicateFilesAsync(scan.Folders, null, CancellationToken.None, options: options);
        var sampleMs = clock.ElapsedMilliseconds;
        CollectionAssert.AreEquivalent(expected, actual);
        Assert.AreEqual(8L, options.Statistics.FilesRejectedBySample);
        Assert.AreEqual(2L, options.Statistics.FullHashesComputed);
        Assert.IsTrue(options.Statistics.BytesRead < before.Statistics.BytesRead / 20);
        var details = new FileComparer().BuildFileComparison(Path.Combine(_root, "a"), Path.Combine(_root, "b"), actual, scan.Cache);
        Assert.HasCount(4, details.Where(f => f.Status == "Different contents").ToList());
        var project = Path.Combine(_root, "samples.cfp"); var data = scan.Cache.ExportToProjectData(new() { _root });
        data.HasAnalysis = true; data.ShowUniqueFiles = true; data.DuplicateFiles = actual; data.Filters.MinimumSizeBytes = 0; data.Filters.MinimumSimilarityPercent = 0;
        await new ProjectManager().SaveProjectAsync(project, data);
        Directory.Move(Path.Combine(_root, "a"), Path.Combine(_root, "a-offline"));
        Directory.Move(Path.Combine(_root, "b"), Path.Combine(_root, "b-offline"));
        using var restored = new MainViewModel(); Assert.IsTrue(await restored.LoadProjectAsync(project));
        await restored.SelectFolderMatchAsync(restored.FilteredFolderMatches.Single());
        Assert.HasCount(4, restored.FileDetails.Where(f => f.Status == "Different contents").ToList());
        TestContext.WriteLine($"SAMPLE: full hashing {before.Statistics.BytesRead:N0} bytes / {baselineMs} ms; sampled {options.Statistics.BytesRead:N0} bytes / {sampleMs} ms; synthetic fixture, OS caches affect timings.");
    }

    [TestMethod]
    public async Task DifferenceOutsideSamples_StillRequiresFullVerification()
    {
        var bytes = new byte[2 * 1024 * 1024];
        Write("a", "same-size", bytes); bytes[256 * 1024] = 1; Write("b", "same-size", bytes);
        var scan = await Scan(); var options = new AnalysisOptions();
        var result = await scan.Analyzer.FindDuplicateFilesAsync(scan.Folders, null, CancellationToken.None, options: options);
        Assert.IsEmpty(result); Assert.AreEqual(2L, options.Statistics.FilesSampled);
        Assert.AreEqual(2L, options.Statistics.FullHashesComputed); Assert.AreEqual(0L, options.Statistics.FilesRejectedBySample);
    }

    [TestMethod]
    public async Task EmptyFileCombinations_AreJoinedOnlyForQualifiedFolders()
    {
        const int copies = 600;
        for (var i = 0; i < copies; i++) Write($"copy-{i:D3}", "empty", []);
        Write("copy-000", "shared", [1]); Write("copy-001", "shared", [1]);
        var baseline = await Scan(); var before = new AnalysisOptions { RetainFileMatches = false };
        var clock = Stopwatch.StartNew();
        await baseline.Analyzer.FindDuplicateFilesAsync(baseline.Folders, null, CancellationToken.None, options: before);
        var baselineMs = clock.ElapsedMilliseconds;
        var scan = await Scan(); var options = new AnalysisOptions { QualifyingFoldersOnly = true };
        clock.Restart();
        var files = await scan.Analyzer.FindDuplicateFilesAsync(scan.Folders, null, CancellationToken.None, options: options);
        Assert.HasCount(2, files); Assert.AreEqual(2L, options.Statistics.FilePairsEmitted);
        Assert.AreEqual(copies * (copies - 1L) / 2 + 1, before.Statistics.FilePairsEmitted);
        Assert.AreEqual(2L, options.Statistics.FullHashesComputed); Assert.AreEqual(2L, options.Statistics.BytesRead);
        Assert.HasCount(2, (await scan.Analyzer.AggregateFolderMatchesAsync(files, scan.Cache)).Single().DuplicateFiles);
        TestContext.WriteLine($"EMPTY: {copies} folders, {before.Statistics.FilePairsEmitted:N0} legacy pairs / {baselineMs} ms versus {options.Statistics.FilePairsEmitted} qualified pairs / {clock.ElapsedMilliseconds} ms.");
    }

    [TestMethod]
    public async Task CachedAndUniqueFiles_RequireNoContentReads_AndDoNotWaitForResultConsumer()
    {
        for (var i = 0; i < 80; i++)
        { Write("a", $"match-{i}", [(byte)i]); Write("b", $"match-{i}", [(byte)i]); Write("a", $"unique-{i}", [1]); }
        var scan = await Scan();
        var expected = await scan.Analyzer.FindDuplicateFilesAsync(scan.Folders, null, CancellationToken.None);
        var options = new AnalysisOptions { RetainFileMatches = false };
        var buffer = new AnalysisResultBuffer();
        var result = await scan.Analyzer.FindDuplicateFilesAsync(scan.Folders, null, CancellationToken.None,
            (batch, _) => { buffer.Add(batch); return Task.CompletedTask; }, options).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsEmpty(result); Assert.IsTrue(buffer.HasChanges, "Verification completed with no result consumer running.");
        Assert.AreEqual(0L, options.Statistics.BytesRead); Assert.AreEqual(0L, options.Statistics.FullHashesComputed);
        Assert.AreEqual(160L, options.Statistics.CachedHashesUsed);
        var rows = buffer.Take(); Assert.HasCount(1, rows);
        CollectionAssert.AreEquivalent(expected, rows.Single().Files); Assert.IsFalse(buffer.HasChanges);
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task BlockedUi_DoesNotBlockVerificationWorkers()
    {
        for (var folder = 0; folder < 4; folder++)
            for (var file = 0; file < 30; file++) Write($"copy-{folder}", $"match-{file:D2}", new byte[1024]);
        FileStream? locked = File.Open(Path.Combine(_root, "copy-0", "match-29"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var model = new MainViewModel(); Assert.IsTrue(await model.AddFolderAsync(_root));
        var observed = false; var verifiedWhileUiBlocked = false;
        void OnRows(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (observed || !model.IsLivePreview || args.NewItems is not { Count: > 0 }) return;
            observed = true;
            Assert.IsTrue(model.LastAnalysisStatistics!.FullHashesComputed < 120);
            locked!.Dispose(); locked = null;
            // Deliberately stall the STA consumer: the old eight-batch channel stalled producers here.
            verifiedWhileUiBlocked = SpinWait.SpinUntil(() => model.LastAnalysisStatistics.FullHashesComputed == 120, TimeSpan.FromSeconds(10));
        }
        model.FilteredFolderMatches.CollectionChanged += OnRows;
        try
        {
            Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
            Assert.IsTrue(observed); Assert.IsTrue(verifiedWhileUiBlocked, "Hashing must complete even when the UI thread is blocked.");
            Assert.HasCount(6, model.FilteredFolderMatches);
            Assert.IsTrue(model.FilteredFolderMatches.All(m => m.DuplicateFiles.Count == 30));
        }
        finally { locked?.Dispose(); model.FilteredFolderMatches.CollectionChanged -= OnRows; }
    }
}
