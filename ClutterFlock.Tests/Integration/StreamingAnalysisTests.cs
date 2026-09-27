using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Services;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
public sealed class StreamingAnalysisTests
{
    private string _root = null!;
    public TestContext TestContext { get; set; } = null!;
    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "ClutterFlockStreaming", Guid.NewGuid().ToString("N")));
    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockStreaming")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, true);
    }
    private string Write(string folder, string name, string content)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        var path = Path.Combine(dir, name); File.WriteAllText(path, content); return path;
    }
    private async Task<(ICacheManager Cache, IErrorRecoveryService Errors, List<string> Folders)> Scan()
    {
        ICacheManager cache = new CacheManager(); IErrorRecoveryService errors = new ErrorRecoveryService();
        var folders = await new FolderScanner(cache, errors).ScanFolderHierarchyAsync(_root, null, CancellationToken.None);
        return (cache, errors, folders);
    }

    [TestMethod]
    public async Task VerifiedBatches_ArriveBeforeCompletion_AndConsumerBackpressureCanBeCancelled()
    {
        for (var i = 0; i < 30; i++) Write($"copy-{i:D2}", "same.dat", "same verified data");
        var scan = await Scan();
        IDuplicateAnalyzer analyzer = new DuplicateAnalyzer(scan.Cache, scan.Errors);
        using var stop = new CancellationTokenSource();
        var first = new TaskCompletionSource<IReadOnlyList<FileMatch>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = analyzer.FindDuplicateFilesAsync(scan.Folders, null, stop.Token, async (batch, token) =>
        {
            first.TrySetResult(batch);
            await release.Task.WaitAsync(token);
        });
        try
        {
            var batch = await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(running.IsCompleted, "The consumer must receive matches while analysis is still active.");
            Assert.IsTrue(batch.Count is > 0 and <= 256);
            foreach (var match in batch)
                Assert.AreEqual(scan.Cache.GetFileHash(match.PathA), scan.Cache.GetFileHash(match.PathB));
            stop.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => running);
        }
        finally { stop.Cancel(); release.TrySetResult(); try { await running; } catch (OperationCanceledException) { } }
    }

    [TestMethod]
    public async Task StreamingAndFinalResults_Agree_AndProgressDescribesActualReadWork()
    {
        for (var folder = 0; folder < 6; folder++)
            for (var file = 0; file < 120; file++) Write($"copy-{folder}", $"document-{file:D3}.dat", new string((char)('a' + file % 20), 4096));
        Write("copy-0", "different", "AAAA"); Write("copy-1", "different", "BBBB");
        Write("copy-0", "renamed", "contents"); Write("copy-1", "other-name", "contents");
        var scan = await Scan();
        var updates = new ConcurrentQueue<AnalysisProgress>();
        var streamed = new ConcurrentBag<FileMatch>();
        var clock = Stopwatch.StartNew(); long firstAt = -1;
        IDuplicateAnalyzer analyzer = new DuplicateAnalyzer(scan.Cache, scan.Errors);
        var result = await analyzer.FindDuplicateFilesAsync(scan.Folders, new InlineProgress(updates.Enqueue), CancellationToken.None,
            (batch, _) => { Interlocked.CompareExchange(ref firstAt, clock.ElapsedMilliseconds, -1); foreach (var match in batch) streamed.Add(match); return Task.CompletedTask; });
        Assert.HasCount(1800, result); Assert.HasCount(result.Count, streamed);
        CollectionAssert.AreEquivalent(result, streamed.ToList()); Assert.HasCount(result.Count, result.Distinct().ToList());
        Assert.IsTrue(updates.Any(p => p.StatusMessage.Contains("MiB read") && p.StatusMessage.Contains("MiB/s")));
        Assert.IsFalse(scan.Errors.GetErrorSummary().HasErrors);
        TestContext.WriteLine($"STREAM: {result.Count} verified pairs; first batch {firstAt} ms; completed {clock.ElapsedMilliseconds} ms. Synthetic 720-file fixture, OS caches/hardware affect timings.");
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task CancelLivePreview_RestoresCompletedAnalysisSelectionAndSaveState()
    {
        Write("a", "one.txt", "one"); Write("b", "one.txt", "one");
        using var model = new MainViewModel(); Assert.IsTrue(await model.AddFolderAsync(_root)); Assert.IsTrue(await model.RunComparisonAsync());
        var prior = model.FilteredFolderMatches.Single(); await model.SelectFolderMatchAsync(prior);
        var project = Path.Combine(_root, "project.cfp"); Assert.IsTrue(await model.SaveProjectAsync(project));
        for (var i = 0; i < 100; i++) { Write("a", $"new-{i:D3}", "same"); Write("b", $"new-{i:D3}", "same"); }
        var observed = false;
        void OnRows(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!model.IsLivePreview || args.NewItems is not { Count: > 0 }) return;
            observed = true;
            Assert.IsTrue(model.CanEditWorkspace); Assert.IsTrue(model.CanApplyFilters);
            Assert.IsFalse(model.CanSaveProject); Assert.IsFalse(model.CanAddFolders);
            Assert.IsTrue(((FolderMatch)args.NewItems[0]!).IsProvisional);
            StringAssert.Contains(model.SnapshotSummary, "LIVE RESULTS");
            model.CancelOperation();
        }
        model.FilteredFolderMatches.CollectionChanged += OnRows;
        Assert.IsFalse(await model.RunComparisonAsync());
        model.FilteredFolderMatches.CollectionChanged -= OnRows;
        Assert.IsTrue(observed); Assert.AreSame(prior, model.FilteredFolderMatches.Single());
        Assert.AreSame(prior, model.SelectedFolderMatch); Assert.HasCount(1, model.FileDetails);
        Assert.IsFalse(model.IsLivePreview); Assert.IsFalse(model.IsAnalyzing); Assert.IsFalse(model.IsDirty);
        Assert.IsTrue(await model.SaveProjectAsync(project));
        Assert.HasCount(1, (await new ProjectManager().LoadProjectAsync(project)).DuplicateFiles);
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task LiveResults_CanBeSelectedAndFiltered_AndCompleteIntoSavableEvidence()
    {
        for (var i = 0; i < 60; i++) { Write("a", $"same-{i:D3}", "same"); Write("b", $"same-{i:D3}", "same"); }
        using var model = new MainViewModel(); Assert.IsTrue(await model.AddFolderAsync(_root));
        FolderMatch? preview = null;
        Task? selection = null;
        Task? filtering = null;
        void OnRows(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (preview != null || !model.IsLivePreview || args.NewItems is not { Count: > 0 }) return;
            preview = (FolderMatch)args.NewItems[0]!;
            Assert.AreEqual("Verification in progress", preview.Relationship);
            selection = model.SelectFolderMatchAsync(preview);
            model.FileView = "Identical";
            model.FolderSearch = "a";
            filtering = model.ApplyFiltersAsync();
        }
        model.FilteredFolderMatches.CollectionChanged += OnRows;
        Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
        model.FilteredFolderMatches.CollectionChanged -= OnRows;
        Assert.IsNotNull(preview); if (selection != null) await selection; if (filtering != null) await filtering;
        Assert.AreSame(preview, model.FilteredFolderMatches.Single()); Assert.IsFalse(preview.IsProvisional);
        Assert.AreEqual("Identical files", preview.Relationship); Assert.AreEqual(100, preview.SimilarityPercentage);
        await model.SelectFolderMatchAsync(preview); Assert.HasCount(60, model.FileDetails);
        Assert.IsTrue(model.CanReview); Assert.IsTrue(model.CanSaveProject);
        var project = Path.Combine(_root, "complete.cfp"); Assert.IsTrue(await model.SaveProjectAsync(project));
        using var restored = new MainViewModel(); Assert.IsTrue(await restored.LoadProjectAsync(project));
        Assert.HasCount(60, restored.FilteredFolderMatches.Single().DuplicateFiles);
    }

    [TestMethod]
    public async Task ChangedFileAfterDiscovery_IsNotPublishedAsVerified()
    {
        Write("a", "same", "same"); var changed = Write("b", "same", "same");
        var scan = await Scan(); File.WriteAllText(changed, "changed length");
        var batches = new ConcurrentBag<FileMatch>();
        var result = await new DuplicateAnalyzer(scan.Cache, scan.Errors).FindDuplicateFilesAsync(scan.Folders, null, CancellationToken.None,
            (batch, _) => { foreach (var match in batch) batches.Add(match); return Task.CompletedTask; });
        Assert.IsEmpty(result); Assert.IsEmpty(batches); Assert.IsTrue(scan.Errors.GetErrorSummary().HasErrors);
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task Window_LivePreviewEnablesBrowsing_AndLabelsUnfinishedEvidence()
    {
        Write("a", "report.txt", "same report"); Write("b", "report.txt", "same report");
        var locks = new List<FileStream>();
        for (var i = 0; i < 6; i++)
        {
            var path = Write("a", $"pending-{i}.bin", new string('x', 8192));
            Write("b", $"pending-{i}.bin", new string('x', 8192));
            locks.Add(new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        }
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000, Width = 1500, Height = 960 };
        window.Show(); window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var model = (MainViewModel)window.DataContext;
        Task<bool>? running = null;
        try
        {
            Assert.IsTrue(await model.AddFolderAsync(_root));
            var first = new TaskCompletionSource<FolderMatch>(TaskCreationOptions.RunContinuationsAsynchronously);
            model.FilteredFolderMatches.CollectionChanged += (_, args) =>
            {
                if (model.IsLivePreview && args.NewItems is { Count: > 0 }) first.TrySetResult((FolderMatch)args.NewItems[0]!);
            };
            var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(model.StatusMessage) && model.StatusMessage.Contains("1 verified file pairs")) reported.TrySetResult();
            };
            running = model.RunComparisonAsync();
            var pair = await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await model.SelectFolderMatchAsync(pair);
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(10));
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Assert.IsFalse(running.IsCompleted); Assert.IsTrue(model.IsLivePreview);
            Assert.IsTrue(((System.Windows.Controls.Grid)window.FindName("workspaceGrid")).IsEnabled);
            Assert.IsTrue(((System.Windows.Controls.Button)window.FindName("btnApplyFilters")).IsEnabled);
            Assert.IsFalse(((System.Windows.Controls.Button)window.FindName("btnSaveProject")).IsEnabled);
            Assert.IsTrue(model.FileDetails.Any(f => f.Status == "Unverified"));
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ClutterFlock.sln"))) directory = directory.Parent;
            Assert.IsNotNull(directory);
            var output = Path.Combine(directory.FullName, "artifacts", "ui-review"); Directory.CreateDirectory(output);
            var content = (System.Windows.FrameworkElement)window.Content;
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(content);
            var image = new System.Windows.Media.Imaging.PngBitmapEncoder(); image.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, "live-analysis.png"))) image.Save(file);
            model.FileSearch = "report";
            model.CancelOperation(); Assert.IsFalse(await running);
            Assert.IsEmpty(model.FilteredFolderMatches); Assert.IsTrue(model.IsDirty);
        }
        finally
        {
            model.CancelOperation(); if (running != null) await running;
            window.Hide(); window.Close(); foreach (var file in locks) file.Dispose();
        }
    }

    private sealed class InlineProgress(Action<AnalysisProgress> report) : IProgress<AnalysisProgress>
    { public void Report(AnalysisProgress value) => report(value); }
}
