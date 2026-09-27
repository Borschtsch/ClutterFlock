using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class WorkspaceExperienceTests
{
    private string _root = null!;
    private string A => Path.Combine(_root, "Desktop archive", "Research project");
    private string B => Path.Combine(_root, "External drive", "Research project");
    private string Project => Path.Combine(_root, "Lifetime files.cfp");
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ClutterFlockWorkspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(A); Directory.CreateDirectory(B);
        foreach (var name in new[] { "report.docx", "source.cs", "mail.mbox" })
        { File.WriteAllText(Path.Combine(A, name), name); File.WriteAllText(Path.Combine(B, name), name); }
        File.WriteAllText(Path.Combine(A, "draft.txt"), "aaaa"); File.WriteAllText(Path.Combine(B, "draft.txt"), "bbbb");
        File.WriteAllText(Path.Combine(A, "notes.md"), "Keep the original notes.");
        File.WriteAllText(Path.Combine(B, "export.csv"), "row,value");
    }
    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockWorkspace")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, true);
    }
    private async Task<MainViewModel> Analyze()
    {
        var model = new MainViewModel();
        Assert.IsTrue(await model.AddFolderAsync(A)); Assert.IsTrue(await model.AddFolderAsync(B));
        Assert.IsTrue(await model.RunComparisonAsync(), model.StatusMessage);
        await model.SelectFolderMatchAsync(model.FilteredFolderMatches.Single());
        return model;
    }

    [TestMethod]
    public async Task ReviewAndNavigation_SaveOfflineRestore_ResumeAndRecheckAfterRefresh()
    {
        using var model = await Analyze();
        model.RenameLocation(A, "Old desktop");
        model.RenameLocation(B, "External archive");
        model.ReviewStatus = "Investigate"; model.IsBookmarked = true;
        model.ReviewNotes = "Original source and correspondence. Keep the handwritten notes export.";
        model.FolderSearch = "Research"; model.FocusFolder = A; model.LocationFilter = A;
        model.ReviewFilter = "Bookmarked"; model.FileView = "Differences"; model.FileSearch = "draft";
        model.LocationsWidth = 250; model.ComparisonsWidth = 430;
        await model.ApplyFiltersAsync();
        Assert.HasCount(1, model.FileDetails); Assert.AreEqual("Different contents", model.FileDetails[0].Status);
        Assert.IsTrue(await model.SaveProjectAsync(Project), model.StatusMessage); Assert.IsFalse(model.IsDirty);
        Directory.Move(A, A + "-offline"); Directory.Move(B, B + "-offline");
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(Project), restored.StatusMessage);
        Assert.IsFalse(restored.IsDirty); Assert.AreEqual("Old desktop", restored.Locations[0].DisplayName);
        Assert.AreEqual(model.ReviewNotes, restored.ReviewNotes); Assert.AreEqual("Investigate", restored.ReviewStatus);
        Assert.IsTrue(restored.IsBookmarked); Assert.AreEqual("Differences", restored.FileView);
        Assert.AreEqual("draft", restored.FileSearch); Assert.AreEqual(A, restored.FocusFolder);
        Assert.AreEqual(250, restored.LocationsWidth); Assert.AreEqual(430, restored.ComparisonsWidth);
        Assert.HasCount(1, restored.FileDetails); Assert.HasCount(1, restored.FolderCatalog);
        StringAssert.Contains(restored.ProjectTitle, "Lifetime files"); StringAssert.Contains(restored.SaveSummary, "Saved");
        Assert.IsFalse(await restored.RunComparisonAsync()); Assert.HasCount(1, restored.FilteredFolderMatches);
        Directory.Move(A + "-offline", A); Directory.Move(B + "-offline", B);
        await restored.ResetFiltersAsync(); Assert.IsTrue(await restored.RunComparisonAsync());
        await restored.SelectFolderMatchAsync(restored.FilteredFolderMatches.Single());
        StringAssert.Contains(restored.ReviewNotice, "refreshed");
        restored.ReviewStatus = "Reviewed"; Assert.IsFalse(restored.ReviewNotice.Contains("refreshed"));
        Assert.IsTrue(restored.IsDirty);
        restored.NewProject(); Assert.IsEmpty(restored.ScanFolders); Assert.IsEmpty(restored.FileDetails);
        Assert.IsFalse(restored.IsDirty); Assert.AreEqual("", restored.ProjectPath);
    }

    [TestMethod]
    public async Task PreparedLocations_DoNotScanOrDiscardEvidence_AndSurviveSave()
    {
        using var model = await Analyze();
        var original = model.FilteredFolderMatches.Single();
        model.RemoveFolder(A); model.RemoveFolder(B);
        Assert.IsTrue(model.RootsChanged); Assert.AreSame(original, model.FilteredFolderMatches.Single());
        Assert.IsFalse(model.CanRunComparison); Assert.IsTrue(model.CanSaveProject);
        Assert.IsTrue(await model.SaveProjectAsync(Project), model.StatusMessage);
        using var restored = new MainViewModel();
        Assert.IsTrue(await restored.LoadProjectAsync(Project)); Assert.IsEmpty(restored.ScanFolders);
        Assert.HasCount(1, restored.FilteredFolderMatches); Assert.IsTrue(restored.RootsChanged);
        Assert.IsTrue(await restored.AddFolderAsync(A)); Assert.IsTrue(await restored.RunComparisonAsync());
        Assert.IsEmpty(restored.FilteredFolderMatches); Assert.IsFalse(restored.RootsChanged);
        restored.NewProject(); Assert.IsTrue(await restored.AddFolderAsync(B));
        Assert.IsTrue(await restored.SaveProjectAsync(Project));
        Assert.IsEmpty((await new ProjectManager().LoadProjectAsync(Project)).FolderInfoCache);
    }

    [TestMethod]
    public async Task FileEvidence_FiltersSeparateOneSidedChangedAndUnverifiedFiles()
    {
        var lockedPath = Path.Combine(A, "locked.bin");
        File.WriteAllText(lockedPath, "locked"); File.WriteAllText(Path.Combine(B, "locked.bin"), "locked");
        using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var model = await Analyze();
        Assert.HasCount(7, model.FileDetails);
        foreach (var view in new[] { "Only A", "Only B", "Different contents", "Unverified" })
        { model.FileView = view; Assert.HasCount(1, model.FileDetails); Assert.AreEqual(view, model.FileDetails[0].Status); }
        model.FileView = "Identical"; Assert.HasCount(3, model.FileDetails); Assert.IsFalse(model.ShowUniqueFiles);
        model.FileView = "Differences"; Assert.HasCount(4, model.FileDetails);
        model.FileSearch = "locked"; Assert.HasCount(1, model.FileDetails);
        StringAssert.Contains(model.CoverageSummary, "Incomplete"); StringAssert.Contains(model.IssuesText, "locked.bin");
        Assert.IsTrue(await model.SaveProjectAsync(Project), model.StatusMessage);
        using var restored = new MainViewModel(); Assert.IsTrue(await restored.LoadProjectAsync(Project));
        StringAssert.Contains(restored.CoverageSummary, "Incomplete");
        Assert.AreEqual("Unverified", restored.FileDetails.Single().Status);
        model.FileSearch = ""; model.ShowUniqueFiles = true; model.FileView = "All";
        Assert.HasCount(7, model.FileDetails);
    }

    [TestMethod]
    public async Task LowSimilarityContainment_RemainsDiscoverable_AndCanBeNavigatedByEitherFolder()
    {
        File.Delete(Path.Combine(A, "notes.md")); File.Delete(Path.Combine(A, "draft.txt"));
        for (var i = 0; i < 10; i++) File.WriteAllText(Path.Combine(B, $"extra-{i}.txt"), "extra");
        using var model = await Analyze();
        var pair = model.FilteredFolderMatches.Single();
        Assert.IsTrue(pair.SimilarityPercentage < 50); Assert.AreEqual("B contains all of A", pair.Relationship);
        Assert.AreEqual(100, pair.LeftCoverage); Assert.IsTrue(pair.RightCoverage < 50);
        model.RelationshipFilter = "Containment"; await model.ApplyFiltersAsync(); Assert.HasCount(1, model.FilteredFolderMatches);
        model.FocusFolder = B; await model.ApplyFiltersAsync(); Assert.HasCount(1, model.FilteredFolderMatches);
        model.FolderSearch = "missing"; await model.ApplyFiltersAsync(); Assert.IsEmpty(model.FilteredFolderMatches);
        StringAssert.Contains(model.EmptyResultsMessage, "filters");
        await model.ResetFiltersAsync(); Assert.HasCount(1, model.FilteredFolderMatches); Assert.HasCount(2, model.FolderCatalog);
        model.RelationshipFilter = "Identical files"; await model.ApplyFiltersAsync(); Assert.IsEmpty(model.FilteredFolderMatches);
    }

    [TestMethod]
    public async Task LegacySnapshot_DisplaysUnknownCompleteness_InsteadOfImplyingVerifiedCoverage()
    {
        using var model = await Analyze(); Assert.IsTrue(await model.SaveProjectAsync(Project), model.StatusMessage);
        var manager = new ProjectManager(); var data = await manager.LoadProjectAsync(Project); data.Workspace = null;
        await manager.SaveProjectAsync(Project, data);
        using var restored = new MainViewModel(); Assert.IsTrue(await restored.LoadProjectAsync(Project));
        StringAssert.Contains(restored.CoverageSummary, "not recorded");
        StringAssert.Contains(restored.SnapshotSummary, "date unknown");
        Assert.HasCount(2, restored.Locations);
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task Window_ExploreFilterReviewAndSort_ProducesReadableWorkspace()
    {
        using (var original = await Analyze())
        {
            original.RenameLocation(A, "Desktop archive"); original.RenameLocation(B, "External drive");
            Assert.IsTrue(await original.SaveProjectAsync(Project));
        }
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        try
        {
            var model = (MainViewModel)window.DataContext;
            Assert.IsTrue(await model.LoadProjectAsync(Project));
            window.Width = 1500; window.Height = 960;
            window.Measure(new Size(1500, 960)); window.Arrange(new Rect(0, 0, 1500, 960)); window.UpdateLayout();
            ((ComboBox)window.FindName("fileView")).SelectedItem = "Differences";
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.HasCount(3, model.FileDetails);
            model.ReviewStatus = "Investigate"; model.ReviewNotes = "Compare draft versions before deciding what to retain.";
            model.IsBookmarked = true;
            window.UpdateLayout();
            var fileList = (ListView)window.FindName("listViewFiles");
            var sizeHeader = Descendants<GridViewColumnHeader>(fileList).First(h => h.Content?.ToString() == "A size");
            sizeHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var sizes = fileList.Items.Cast<FileDetailInfo>().Select(f => f.LeftSizeBytes).ToList();
            CollectionAssert.AreEqual(sizes.OrderBy(s => s).ToList(), sizes);
            sizeHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            sizes = fileList.Items.Cast<FileDetailInfo>().Select(f => f.LeftSizeBytes).ToList();
            CollectionAssert.AreEqual(sizes.OrderByDescending(s => s).ToList(), sizes);
            ((Button)window.FindName("btnClearFileSort")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var invalid = (TextBox)window.FindName("txtMinSimilarity"); invalid.Text = "oops";
            ((Button)window.FindName("btnApplyFilters")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(((TextBlock)window.FindName("filterError")).Text.Length > 0);
            Assert.HasCount(1, model.FilteredFolderMatches); invalid.Text = "0";
            ((Expander)window.FindName("reviewExpander")).IsExpanded = true;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var output = Path.Combine(FindRepo(), "artifacts", "ui-review"); Directory.CreateDirectory(output);
            Render(window, Path.Combine(output, "workspace.png"), 1500, 960);
            ((Expander)window.FindName("reviewExpander")).IsExpanded = false;
            window.Width = 1150; window.Height = 720; window.Measure(new Size(1150, 720)); window.Arrange(new Rect(0, 0, 1150, 720)); window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Render(window, Path.Combine(output, "workspace-small.png"), 1150, 720);
            ((ComboBox)window.FindName("relationshipFilter")).SelectedItem = "Identical files";
            await WaitFor(() => !model.IsPopulatingResults && model.FilteredFolderMatches.Count == 0);
            Assert.AreEqual("Identical files", model.RelationshipFilter);
            ((ComboBox)window.FindName("relationshipFilter")).SelectedItem = "All relationships";
            await WaitFor(() => !model.IsPopulatingResults && model.FilteredFolderMatches.Count == 1);
            Assert.IsNull(window.FindName("folderCatalog"));
            ((Expander)window.FindName("locationsExpander")).IsExpanded = true;
            ((ListBox)window.FindName("listBoxFolders")).SelectedIndex = 1;
            await WaitFor(() => !model.IsPopulatingResults && model.LocationFilter == B);
            Assert.HasCount(1, model.FilteredFolderMatches);
            ((ComboBox)window.FindName("reviewFilter")).SelectedItem = "Bookmarked";
            await WaitFor(() => !model.IsPopulatingResults && model.ReviewFilter == "Bookmarked");
            Assert.HasCount(1, model.FilteredFolderMatches);
            await model.ResetFiltersAsync();
            await model.SelectFolderMatchAsync(model.FilteredFolderMatches.Single());
            Assert.IsTrue(await model.SaveProjectAsync(Project), model.StatusMessage);
            File.Copy(Project, Path.Combine(output, "example-workspace.cfp"), overwrite: true);
            model.NewProject();
            window.Width = 1500; window.Height = 960;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            Render(window, Path.Combine(output, "workspace-empty.png"), 1500, 960);
        }
        finally { window.Hide(); window.Close(); }
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.IsTrue(condition(), "The UI operation did not reach the expected state.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject item) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
        { var child = VisualTreeHelper.GetChild(item, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClutterFlock.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository not found");
    }
    private static void Render(Window window, string path, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
