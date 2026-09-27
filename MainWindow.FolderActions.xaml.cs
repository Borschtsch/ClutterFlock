using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClutterFlock.Core;
using ClutterFlock.Models;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;

namespace ClutterFlock;

public partial class MainWindow
{
    private bool _folderActionPending;

    private async void FolderAction_Click(object sender, RoutedEventArgs e)
    {
        if (_folderActionPending || !_viewModel.CanManageFolders || (sender as Button)?.Tag is not string name
            || !Enum.TryParse<FolderAction>(name, out var action)) return;
        _folderActionPending = true;
        try
        {
            var plan = await _viewModel.PrepareFolderActionAsync(action);
            if (plan == null) return;
            if (plan.Subfolders.Count > 0) { ShowSubfolderReview(plan); return; }
            if (plan.Conflicts.Count > 0) { ShowConflictReview(plan); return; }
            var confirm = new ActionConfirmationWindow(plan.Title, plan.Description,
                plan.Destination == null ? "Permanently delete folder" : plan.Title, (Style)FindResource("DangerButton")) { Owner = this };
            if (confirm.ShowDialog() == true) await _viewModel.ExecuteFolderActionAsync(plan);
        }
        catch (Exception ex) { _viewModel.StatusMessage = $"Folder action failed: {ex.Message}"; }
        finally { _folderActionPending = false; }
    }

    private void FileSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (btnOpenFileA == null) return;
        var row = listViewFiles.SelectedItem as FileDetailInfo;
        btnOpenFileA.IsEnabled = btnDeleteFileA.IsEnabled = row?.HasLeftFile == true;
        btnOpenFileB.IsEnabled = btnDeleteFileB.IsEnabled = row?.HasRightFile == true;
    }

    private async void FileAction_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanManageFolders || listViewFiles.SelectedItem is not FileDetailInfo row || (sender as Button)?.Tag is not string action) return;
        var path = action.EndsWith('A') ? row.LeftFullPath : row.RightFullPath;
        if (string.IsNullOrEmpty(path)) return;
        if (action.StartsWith("Open")) OpenItem(path);
        else await ConfirmDeleteFileAsync(path, this);
    }

    private void OpenItem(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) { _viewModel.StatusMessage = $"Item unavailable: {path}"; return; }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { _viewModel.StatusMessage = $"Could not open item: {ex.Message}"; }
    }

    private async Task<bool> ConfirmDeleteFileAsync(string path, Window owner)
    {
        if (!_viewModel.CanManageFolders) return false;
        var confirmation = new ActionConfirmationWindow("Delete file permanently",
            $"Permanently delete this file:\n\n{path}\n\nOnly this file will be deleted. The other copy is kept. This bypasses the Recycle Bin and cannot be undone.",
            "Permanently delete file", (Style)FindResource("DangerButton")) { Owner = owner };
        if (confirmation.ShowDialog() != true) return false;
        return await _viewModel.DeleteFileAsync(path);
    }

    private void ShowSubfolderReview(FolderOperationPlan plan)
    {
        var (window, panel) = ReviewWindow("Subfolders must be resolved first",
            "This action is blocked. Select a child folder to inspect its existing comparisons or open it in Explorer. Resolve child folders individually; nothing here runs recursively.");
        var list = new ListBox { ItemsSource = plan.Subfolders.Select(f => new ListBoxItem { Content = $"{f.Side} · {f.Path}", Tag = f }).ToList() };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var inspect = new Button { Content = "Show folder comparisons", Padding = new Thickness(12, 6, 12, 6) };
        var open = new Button { Content = "Open folder", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        inspect.Click += async (_, _) =>
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is not BlockingFolder folder) return;
            window.Close();
            await NavigateToFolderAsync(folder.Path);
            if (_viewModel.FilteredFolderMatches.Count == 0) _viewModel.StatusMessage = "No recorded matches for this child folder. Inspect it manually or compare / refresh; no files were changed.";
        };
        open.Click += (_, _) => { if ((list.SelectedItem as ListBoxItem)?.Tag is BlockingFolder folder) OpenItem(folder.Path); };
        buttons.Children.Add(inspect); buttons.Children.Add(open); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(list);
        window.ShowDialog();
    }

    private void ShowConflictReview(FolderOperationPlan plan)
    {
        var (window, panel) = ReviewWindow("Resolve file conflicts before merging",
            "These conflicts were checked against current file contents. Select a file, open A or B to inspect it, then explicitly delete the version you do not want. Close this window and retry merge when finished.");
        var rows = new ObservableCollection<ListBoxItem>(plan.Conflicts.Select(c => new ListBoxItem
        { Content = $"{Path.GetFileName(c.PathA)} · {c.Reason}\nA: {c.PathA}\nB: {c.PathB}", Tag = c, Padding = new Thickness(4, 8, 4, 8) }));
        var list = new ListBox { ItemsSource = rows, Name = "ConflictList" };
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), IsEnabled = false };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        list.SelectionChanged += (_, _) => buttons.IsEnabled = list.SelectedItem != null && _viewModel.CanManageFolders;
        foreach (var action in new[] { "Open A", "Open B", "Delete A…", "Delete B…" })
        {
            var button = new Button { Content = action, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            if (action.StartsWith("Delete"))
            {
                button.Style = (Style)FindResource("DangerButton");
                if (action.Contains('A')) button.Margin = new Thickness(32, 0, 8, 0);
            }
            button.Click += async (_, _) =>
            {
                if (list.SelectedItem is not ListBoxItem row || row.Tag is not FileConflict conflict) return;
                var path = action.Contains('A') ? conflict.PathA : conflict.PathB;
                if (action.StartsWith("Open")) { OpenItem(path); return; }
                buttons.IsEnabled = false;
                try
                {
                    if (await ConfirmDeleteFileAsync(path, window)) rows.Remove(row);
                    status.Text = rows.Count == 0 ? "Conflicts resolved. Close and retry merge to check the folders again." : _viewModel.StatusMessage;
                }
                finally { buttons.IsEnabled = list.SelectedItem != null && _viewModel.CanManageFolders; }
            };
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(list);
        window.ShowDialog();
    }

    private (Window, DockPanel) ReviewWindow(string title, string description)
    {
        var window = new Window { Title = title, Owner = this, Width = 850, Height = 500, MinWidth = 620, MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new DockPanel { Margin = new Thickness(18) };
        var caption = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(caption, Dock.Top); panel.Children.Add(caption);
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => window.Close(); DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        window.Content = panel; return (window, panel);
    }
}
