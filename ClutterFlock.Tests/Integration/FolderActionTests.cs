using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClutterFlock.Core;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class FolderActionTests
{
    private string _root = null!;
    private string A => Path.Combine(_root, "a");
    private string B => Path.Combine(_root, "b");
    private readonly FolderOperations _operations = new();
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ClutterFlockActions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(A); Directory.CreateDirectory(B);
        File.WriteAllText(Path.Combine(A, "same"), "same"); File.WriteAllText(Path.Combine(B, "same"), "same");
        File.WriteAllText(Path.Combine(A, "a-only"), "A"); File.WriteAllText(Path.Combine(B, "b-only"), "B");
    }
    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockActions")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, true);
    }

    [TestMethod]
    [DataRow(FolderAction.DeleteA)]
    [DataRow(FolderAction.DeleteB)]
    [DataRow(FolderAction.MergeToA)]
    [DataRow(FolderAction.MergeToB)]
    public async Task LeafActions_UseExactDirection_AndDoNotTouchSibling(FolderAction action)
    {
        var sibling = Directory.CreateDirectory(Path.Combine(_root, "a-old")).FullName;
        File.WriteAllText(Path.Combine(sibling, "keep"), "keep");
        var plan = await _operations.PrepareAsync(action, A, B);
        Assert.IsTrue(plan.CanExecute); Assert.AreEqual(2, plan.FileCount);
        Assert.IsTrue(Directory.Exists(plan.Source), "Preparation cannot mutate files.");
        await _operations.ExecuteAsync(plan);
        Assert.IsFalse(Directory.Exists(plan.Source));
        var survivor = plan.Source == A ? B : A;
        Assert.AreEqual("same", File.ReadAllText(Path.Combine(survivor, "same")));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(sibling, "keep")));
        if (plan.Destination != null)
        {
            Assert.HasCount(3, Directory.GetFiles(survivor));
            Assert.AreEqual("A", File.ReadAllText(Path.Combine(survivor, "a-only")));
            Assert.AreEqual("B", File.ReadAllText(Path.Combine(survivor, "b-only")));
        }
        else Assert.HasCount(2, Directory.GetFiles(survivor));
    }

    [TestMethod]
    [DataRow(FolderAction.DeleteA, "a")]
    [DataRow(FolderAction.DeleteB, "b")]
    [DataRow(FolderAction.MergeToA, "a")]
    [DataRow(FolderAction.MergeToA, "b")]
    [DataRow(FolderAction.MergeToB, "a")]
    [DataRow(FolderAction.MergeToB, "b")]
    public async Task AnySubfolder_BlocksAction_WithoutRecursiveChanges(FolderAction action, string side)
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, side, "child")).FullName;
        File.WriteAllText(Path.Combine(child, "precious"), "keep");
        var plan = await _operations.PrepareAsync(action, A, B);
        Assert.IsFalse(plan.CanExecute); Assert.AreEqual(child, plan.Subfolders.Single().Path);
        await Assert.ThrowsAsync<IOException>(() => _operations.ExecuteAsync(plan));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(child, "precious")));
        Assert.HasCount(2, Directory.GetFiles(A)); Assert.HasCount(2, Directory.GetFiles(B));
    }

    [TestMethod]
    public async Task Conflict_MustBeExplicitlyResolved_ThenMergeCanProceed()
    {
        var a = Path.Combine(A, "conflict.txt"); var b = Path.Combine(B, "conflict.txt");
        File.WriteAllText(a, "AAAA"); File.WriteAllText(b, "BBBB");
        var plan = await _operations.PrepareAsync(FolderAction.MergeToA, A, B);
        Assert.IsFalse(plan.CanExecute); Assert.AreEqual(a, plan.Conflicts.Single().PathA); Assert.AreEqual(b, plan.Conflicts.Single().PathB);
        await Assert.ThrowsAsync<IOException>(() => _operations.ExecuteAsync(plan));
        Assert.AreEqual("AAAA", File.ReadAllText(a)); Assert.AreEqual("BBBB", File.ReadAllText(b));
        await _operations.DeleteFileAsync(a);
        var resolved = await _operations.PrepareAsync(FolderAction.MergeToA, A, B);
        Assert.IsTrue(resolved.CanExecute); await _operations.ExecuteAsync(resolved);
        Assert.AreEqual("BBBB", File.ReadAllText(a)); Assert.IsFalse(Directory.Exists(B));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangeAfterPreparation_BlocksBeforeAnyMutation(bool newSubfolder)
    {
        var plan = await _operations.PrepareAsync(FolderAction.MergeToB, A, B);
        if (newSubfolder) Directory.CreateDirectory(Path.Combine(A, "new-child"));
        else
        {
            var path = Path.Combine(B, "same"); var timestamp = File.GetLastWriteTimeUtc(path);
            File.WriteAllText(path, "DIFF"); File.SetLastWriteTimeUtc(path, timestamp);
        }
        await Assert.ThrowsAsync<IOException>(() => _operations.ExecuteAsync(plan));
        Assert.HasCount(2, Directory.GetFiles(A)); Assert.HasCount(2, Directory.GetFiles(B));
    }

    [TestMethod]
    public async Task InvalidOrLockedPaths_AreRejectedWithoutMutation()
    {
        await Assert.ThrowsAsync<IOException>(() => _operations.PrepareAsync(FolderAction.DeleteA, A, A));
        await Assert.ThrowsAsync<IOException>(() => _operations.PrepareAsync(FolderAction.MergeToB, _root, B));
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _operations.PrepareAsync(FolderAction.DeleteA, Path.Combine(_root, "missing"), B));
        using (var locked = File.Open(Path.Combine(A, "same"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => _operations.PrepareAsync(FolderAction.MergeToB, A, B));
        Assert.HasCount(2, Directory.GetFiles(A)); Assert.HasCount(2, Directory.GetFiles(B));
    }

    [TestMethod]
    public async Task CancelledMerge_PreservesEveryFileInSourceOrDestination()
    {
        var plan = await _operations.PrepareAsync(FolderAction.MergeToB, A, B);
        using var stop = new CancellationTokenSource(); var count = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => _operations.ExecuteAsync(plan,
            new InlineProgress(_ => { if (++count == 2) stop.Cancel(); }), stop.Token));
        Assert.AreEqual("same", File.ReadAllText(Path.Combine(B, "same")));
        Assert.IsTrue(File.Exists(Path.Combine(A, "a-only")) || File.Exists(Path.Combine(B, "a-only")));
        Assert.AreEqual("B", File.ReadAllText(Path.Combine(B, "b-only")));
    }

    private async Task<MainViewModel> Analyze()
    {
        var model = new MainViewModel();
        Assert.IsTrue(await model.AddFolderAsync(A)); Assert.IsTrue(await model.AddFolderAsync(B));
        Assert.IsTrue(await model.RunComparisonAsync()); await model.SelectFolderMatchAsync(model.FilteredFolderMatches.Single());
        return model;
    }

    [TestMethod]
    public async Task Workflow_DeleteFileUpdatesEvidence_AndMergeInvalidatesSavedResults()
    {
        File.WriteAllText(Path.Combine(A, "conflict"), "a"); File.WriteAllText(Path.Combine(B, "conflict"), "b");
        using var model = await Analyze();
        Assert.IsTrue(model.CanManageFolders);
        Assert.IsFalse((await model.PrepareFolderActionAsync(FolderAction.MergeToA))!.CanExecute);
        Assert.IsTrue(await model.DeleteFileAsync(Path.Combine(A, "conflict")), model.StatusMessage);
        Assert.AreEqual("Only B", model.FileDetails.Single(f => f.PrimaryFileName == "conflict").Status);
        var plan = await model.PrepareFolderActionAsync(FolderAction.MergeToA);
        Assert.IsNotNull(plan); Assert.IsTrue(await model.ExecuteFolderActionAsync(plan), model.StatusMessage);
        Assert.IsEmpty(model.FilteredFolderMatches); Assert.IsEmpty(model.FileDetails); Assert.IsTrue(model.IsDirty);
        Assert.HasCount(1, model.ScanFolders); Assert.AreEqual(A, model.ScanFolders[0]);
        var project = Path.Combine(_root, "after.cfp"); Assert.IsTrue(await model.SaveProjectAsync(project));
        using var restored = new MainViewModel(); Assert.IsTrue(await restored.LoadProjectAsync(project));
        Assert.IsEmpty(restored.FilteredFolderMatches); Assert.IsTrue(await restored.RunComparisonAsync());
        Assert.AreEqual("b", File.ReadAllText(Path.Combine(A, "conflict")));
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task VisibleCleanWindow_CloseCompletesWithoutReentrantClosing()
    {
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var closed = false; window.Closed += (_, _) => closed = true;
        window.Show();
        window.Close();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Yield();
        Assert.IsTrue(closed); Assert.IsFalse(window.IsVisible);
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task Window_DeleteRequiresConfirmation_AndCancelDoesNothing()
    {
        using (var model = await Analyze()) Assert.IsTrue(await model.SaveProjectAsync(Path.Combine(_root, "ui.cfp")));
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var viewModel = (MainViewModel)window.DataContext;
        try
        {
            Assert.IsTrue(await viewModel.LoadProjectAsync(Path.Combine(_root, "ui.cfp")));
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var observed = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = window.OwnedWindows.OfType<ActionConfirmationWindow>().FirstOrDefault();
                if (dialog == null) { if (DateTime.UtcNow > deadline) timer.Stop(); return; }
                observed = true;
                var text = Descendants<TextBox>(dialog).Single().Text;
                StringAssert.Contains(text, A); StringAssert.Contains(text, "cannot be undone");
                Assert.IsTrue(Directory.Exists(A));
                dialog.DialogResult = false; timer.Stop();
            };
            timer.Start();
            ((Button)window.FindName("btnDeleteA")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (!observed && DateTime.UtcNow < deadline)
            { window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(10); }
            timer.Stop(); Assert.IsTrue(observed); Assert.HasCount(2, Directory.GetFiles(A));
            Assert.IsNull(window.FindName("folderCatalog"));
            Assert.IsTrue(((GridLength)((System.Windows.Controls.ColumnDefinition)window.FindName("comparisonsColumn")).Width).Value >= 380);
            var files = (ListView)window.FindName("listViewFiles"); files.SelectedIndex = 0;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(((Button)window.FindName("btnOpenFileA")).IsEnabled || ((Button)window.FindName("btnOpenFileB")).IsEnabled);
        }
        finally { window.Hide(); window.Close(); }
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    [DataRow("btnDeleteA", true)]
    [DataRow("btnMergeA", false)]
    public async Task Window_ConfirmedActionExecutes_AndClearsAffectedEvidence(string buttonName, bool delete)
    {
        using (var model = await Analyze()) Assert.IsTrue(await model.SaveProjectAsync(Path.Combine(_root, "ui.cfp")));
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var viewModel = (MainViewModel)window.DataContext;
        try
        {
            Assert.IsTrue(await viewModel.LoadProjectAsync(Path.Combine(_root, "ui.cfp")));
            await RespondToConfirmation(window, () => ((Button)window.FindName(buttonName)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await WaitFor(window, () => !viewModel.OperationInProgress && !Directory.Exists(delete ? A : B));
            Assert.IsEmpty(viewModel.FilteredFolderMatches); Assert.IsEmpty(viewModel.FileDetails);
            Assert.IsTrue(Directory.Exists(delete ? B : A));
        }
        finally { window.Hide(); window.Close(); }
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task Window_ConflictedFileCanBeSelectedAndExplicitlyDeleted_BeforeMerge()
    {
        File.WriteAllText(Path.Combine(A, "conflict"), "a"); File.WriteAllText(Path.Combine(B, "conflict"), "b");
        using (var model = await Analyze()) Assert.IsTrue(await model.SaveProjectAsync(Path.Combine(_root, "ui.cfp")));
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var viewModel = (MainViewModel)window.DataContext;
        try
        {
            Assert.IsTrue(await viewModel.LoadProjectAsync(Path.Combine(_root, "ui.cfp")));
            var files = (ListView)window.FindName("listViewFiles");
            files.SelectedItem = viewModel.FileDetails.Single(f => f.Status == "Different contents");
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(((Button)window.FindName("btnOpenFileA")).IsEnabled);
            Assert.IsTrue(((Button)window.FindName("btnOpenFileB")).IsEnabled);
            await RespondToConfirmation(window, () => ((Button)window.FindName("btnDeleteFileA")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await WaitFor(window, () => !viewModel.OperationInProgress && !File.Exists(Path.Combine(A, "conflict")));
            Assert.AreEqual("b", File.ReadAllText(Path.Combine(B, "conflict")));
            Assert.AreEqual("Only B", viewModel.FileDetails.Single(f => f.PrimaryFileName == "conflict").Status);
            Assert.IsTrue((await viewModel.PrepareFolderActionAsync(FolderAction.MergeToA))!.CanExecute);
        }
        finally { window.Hide(); window.Close(); }
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Window_BlockedMergeOffersManualReview_WithoutConfirmationOrChanges(bool hasSubfolder)
    {
        if (hasSubfolder) Directory.CreateDirectory(Path.Combine(A, "child"));
        else { File.WriteAllText(Path.Combine(A, "conflict"), "a"); File.WriteAllText(Path.Combine(B, "conflict"), "b"); }
        using (var model = await Analyze()) Assert.IsTrue(await model.SaveProjectAsync(Path.Combine(_root, "ui.cfp")));
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var viewModel = (MainViewModel)window.DataContext;
        var observed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        Exception? failure = null;
        timer.Tick += (_, _) =>
        {
            var review = window.OwnedWindows.Cast<Window>().FirstOrDefault();
            if (review == null) return;
            timer.Stop(); observed = true;
            try
            {
                Assert.IsFalse(review is ActionConfirmationWindow);
                Assert.HasCount(1, Descendants<ListBox>(review).Single().Items);
                if (hasSubfolder)
                {
                    Descendants<ListBox>(review).Single().SelectedIndex = 0;
                    Descendants<Button>(review).Single(b => b.Content?.ToString() == "Show folder comparisons")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else
                {
                    Descendants<ListBox>(review).Single().SelectedIndex = 0;
                    Assert.IsTrue(Descendants<Button>(review).Single(b => b.Content?.ToString() == "Open A").IsEnabled);
                    Assert.IsTrue(Descendants<Button>(review).Single(b => b.Content?.ToString() == "Delete B…").IsEnabled);
                    review.Close();
                }
            }
            catch (Exception ex) { failure = ex; review.Close(); }
        };
        try
        {
            Assert.IsTrue(await viewModel.LoadProjectAsync(Path.Combine(_root, "ui.cfp")));
            timer.Start(); ((Button)window.FindName("btnMergeA")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => observed);
            if (failure != null) throw failure;
            if (hasSubfolder) await WaitFor(window, () => viewModel.FocusFolder == Path.Combine(A, "child") && !viewModel.IsPopulatingResults);
            Assert.HasCount(hasSubfolder ? 2 : 3, Directory.GetFiles(A));
            Assert.HasCount(hasSubfolder ? 2 : 3, Directory.GetFiles(B));
        }
        finally { timer.Stop(); window.Hide(); window.Close(); }
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task Window_BlockedDeleteNavigatesDirectly_AndBulkRefreshesLargeSortedResults()
    {
        const int children = 200;
        for (var i = 0; i < children; i++)
        {
            var child = Directory.CreateDirectory(Path.Combine(A, $"child-{i:D3}")).FullName;
            File.WriteAllText(Path.Combine(child, "same"), "same");
        }
        var target = Path.Combine(A, "child-000");
        var project = Path.Combine(_root, "navigation.cfp");
        using (var original = new MainViewModel())
        {
            Assert.IsTrue(await original.AddFolderAsync(_root));
            Assert.IsTrue(await original.RunComparisonAsync(), original.StatusMessage);
            Assert.HasCount((children + 2) * (children + 1) / 2, original.FilteredFolderMatches);
            original.FocusFolder = A; original.LocationFilter = A;
            original.FolderSearch = "a"; original.ReviewFilter = "Unreviewed"; original.MinimumSimilarity = 1;
            await original.ApplyFiltersAsync();
            await original.SelectFolderMatchAsync(original.FilteredFolderMatches.Single(m => m.LeftFolder == A && m.RightFolder == B));
            Assert.IsTrue(await original.SaveProjectAsync(project));
        }
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var model = (MainViewModel)window.DataContext;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        Exception? failure = null;
        var navigated = false;
        var clock = new System.Diagnostics.Stopwatch();
        timer.Tick += (_, _) =>
        {
            var dialog = window.OwnedWindows.Cast<Window>().FirstOrDefault();
            if (dialog == null) return;
            timer.Stop();
            try
            {
                Assert.IsFalse(dialog is ActionConfirmationWindow);
                var list = Descendants<ListBox>(dialog).Single();
                list.SelectedItem = list.Items.Cast<ListBoxItem>().Single(item =>
                    item.Tag is BlockingFolder folder && folder.Path == target);
                clock.Start(); navigated = true;
                Descendants<Button>(dialog).Single(b => b.Content?.ToString() == "Show folder comparisons")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        };
        try
        {
            Assert.IsTrue(await model.LoadProjectAsync(project), model.StatusMessage);
            var results = (ListView)window.FindName("listViewFolderMatches");
            var sort = new System.ComponentModel.SortDescription("RightFolder", System.ComponentModel.ListSortDirection.Descending);
            results.Items.SortDescriptions.Add(sort);
            var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
            var publishedCounts = new List<int>();
            model.FilteredFolderMatches.CollectionChanged += (_, e) =>
            { changes.Add(e.Action); publishedCounts.Add(model.FilteredFolderMatches.Count); };
            timer.Start();
            ((Button)window.FindName("btnDeleteA")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => failure != null || navigated && model.FocusFolder == target && !model.IsPopulatingResults);
            if (failure != null) throw failure;
            clock.Stop();
            // Let bound control changes and the search debounce run, exposing redundant filter passes.
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.HasCount(1, changes, "Navigation must publish only the destination, without an all-results reset.");
            Assert.AreEqual(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, changes.Single());
            Assert.AreEqual(children + 1, publishedCounts.Single());
            Assert.IsTrue(model.FilteredFolderMatches.All(m => m.LeftFolder == target || m.RightFolder == target));
            Assert.IsNull(model.SelectedFolderMatch);
            Assert.AreEqual("All reviews", model.ReviewFilter); Assert.AreEqual("", model.FolderSearch);
            Assert.AreEqual("", model.LocationFilter); Assert.AreEqual(0, model.MinimumSimilarity);
            Console.WriteLine($"Blocked-delete child navigation across 20,301 folder pairs: {clock.ElapsedMilliseconds} ms, {changes.Count} collection notification.");

            var selected = model.FilteredFolderMatches[0];
            await model.SelectFolderMatchAsync(selected);
            changes.Clear(); clock.Restart();
            await model.ResetFiltersAsync();
            clock.Stop();
            Assert.HasCount(20301, results.Items);
            Assert.HasCount(1, changes); Assert.AreEqual(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, changes[0]);
            Assert.AreEqual(sort, results.Items.SortDescriptions.Single());
            var paths = results.Items.Cast<ClutterFlock.Models.FolderMatch>().Select(m => m.RightFolder).ToList();
            CollectionAssert.AreEqual(paths.OrderByDescending(p => p, StringComparer.CurrentCulture).ToList(), paths);
            Assert.AreSame(selected, model.SelectedFolderMatch); Assert.AreSame(selected, results.SelectedItem);
            Console.WriteLine($"Sorted reset of 20,301 folder pairs: {clock.ElapsedMilliseconds} ms, {changes.Count} collection notification.");

            // Two overlapping destinations must leave only the last requested folder visible.
            var first = model.ShowFolderComparisonsAsync(target);
            var last = model.ShowFolderComparisonsAsync(B);
            await Task.WhenAll(first, last);
            Assert.IsTrue(model.FilteredFolderMatches.All(m => m.LeftFolder == B || m.RightFolder == B));
            Assert.AreEqual(B, model.FocusFolder); Assert.IsFalse(model.IsPopulatingResults);
            Assert.HasCount(children, Directory.GetDirectories(A));
            Assert.AreEqual("same", File.ReadAllText(Path.Combine(target, "same")));
            Assert.HasCount(2, Directory.GetFiles(A)); Assert.HasCount(2, Directory.GetFiles(B));
        }
        finally { timer.Stop(); window.Hide(); window.Close(); }
    }

    private static async Task RespondToConfirmation(MainWindow window, Action trigger)
    {
        var seen = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var dialog = window.OwnedWindows.OfType<ActionConfirmationWindow>().FirstOrDefault();
            if (dialog == null) return;
            seen = true; timer.Stop();
            Descendants<Button>(dialog).Single(b => b.Name == "ConfirmAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        try { trigger(); await WaitFor(window, () => seen); }
        finally { timer.Stop(); }
    }

    private static async Task WaitFor(Window window, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        { window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(10); }
        Assert.IsTrue(condition());
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class InlineProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }
}
