using System.Windows.Controls;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class WindowWorkflowTests
{
    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task Window_RestoresControlsResultsAndSelectedDetails()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClutterFlockWindow", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        File.WriteAllText(Path.Combine(root, "left", "same.txt"), "same");
        File.WriteAllText(Path.Combine(root, "right", "same.txt"), "same");
        var project = Path.Combine(root, "project.cfp");
        var window = new MainWindow();
        try
        {
            using (var original = new MainViewModel { MinimumSizeMB = 0, MinimumSimilarity = 75, ShowUniqueFiles = true })
            {
                Assert.IsTrue(await original.AddFolderAsync(root));
                Assert.IsTrue(await original.RunComparisonAsync());
                await original.SelectFolderMatchAsync(original.FilteredFolderMatches.Single());
                Assert.IsTrue(await original.SaveProjectAsync(project));
            }
            var model = (MainViewModel)window.DataContext;
            Assert.IsTrue(await model.LoadProjectAsync(project), model.StatusMessage);
            Assert.AreEqual("75", ((TextBox)window.FindName("txtMinSimilarity")).Text);
            Assert.AreEqual("0", ((TextBox)window.FindName("txtMinSize")).Text);
            Assert.AreEqual(true, ((CheckBox)window.FindName("chkShowUniqueFiles")).IsChecked);
            var results = (ListView)window.FindName("listViewFolderMatches");
            Assert.HasCount(1, results.Items);
            Assert.AreSame(model.SelectedFolderMatch, results.SelectedItem);
            Assert.HasCount(1, ((ListView)window.FindName("listViewFiles")).Items);
            var minimumSimilarity = (TextBox)window.FindName("txtMinSimilarity");
            var minimumSize = (TextBox)window.FindName("txtMinSize");
            // Drive the real filter handler and await its observable completion.
            minimumSize.Text = "2";
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void FilterCompleted(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(model.IsPopulatingResults) && !model.IsPopulatingResults)
                    completed.TrySetResult();
            }
            model.PropertyChanged += FilterCompleted;
            ((Button)window.FindName("btnApplyFilters")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.PropertyChanged -= FilterCompleted;
            Assert.IsEmpty(results.Items);
            Assert.IsEmpty(((ListView)window.FindName("listViewFiles")).Items);
            minimumSize.Text = "0";
            minimumSimilarity.Text = "100";
            completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            model.PropertyChanged += FilterCompleted;
            ((Button)window.FindName("btnApplyFilters")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.PropertyChanged -= FilterCompleted;
            Assert.HasCount(1, results.Items);
            ((Button)window.FindName("btnClearSort")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            ((Button)window.FindName("btnClearFileSort")).RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
            var roots = (ListBox)window.FindName("listBoxFolders");
            roots.SelectedIndex = 0;
            Assert.IsTrue(((Button)window.FindName("btnRemoveFolder")).IsEnabled);
        }
        finally
        {
            window.Close();
            Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockWindow")) + Path.DirectorySeparatorChar, Path.GetFullPath(root));
            Directory.Delete(root, recursive: true);
        }
    }
}
