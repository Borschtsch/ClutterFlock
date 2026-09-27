using System.ComponentModel;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ClutterFlock.Models;

namespace ClutterFlock;

public partial class MainWindow
{
    private bool _changingWorkspaceFilters;
    private bool _allowClose;
    private bool _closePending;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly Dictionary<GridViewColumnHeader, (string Text, ICollectionView View)> _sortHeaders = new();
    private void ConfigureWorkspaceCommands()
    {
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await _viewModel.ApplyFiltersAsync(); };
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, SaveProject_Click, (_, e) => e.CanExecute = _viewModel.CanSaveProject));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, LoadProject_Click, (_, e) => e.CanExecute = _viewModel.CanLoadProject));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, NewProject_Click, (_, e) => e.CanExecute = _viewModel.CanLoadProject));
    }
    private async Task<bool> SaveCurrentAsync(bool saveAs)
    {
        if (!_viewModel.CanSaveProject) return false;
        var path = _viewModel.ProjectPath;
        if (saveAs || path.Length == 0)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "ClutterFlock project (*.cfp)|*.cfp", DefaultExt = "cfp", FileName = Path.GetFileName(path) };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        return await _viewModel.SaveProjectAsync(path);
    }
    private async Task<bool> ConfirmReplaceAsync()
    {
        if (!_viewModel.IsDirty) return true;
        var answer = MessageBox.Show(this, "Save your workspace changes before continuing?", "Unsaved workspace", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && await SaveCurrentAsync(false);
    }
    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync(true);
    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CanLoadProject && await ConfirmReplaceAsync()) _viewModel.NewProject();
    }
    private async void Locations_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths.Where(Directory.Exists)) await _viewModel.AddFolderAsync(path);
    }
    private async void Location_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null || _changingWorkspaceFilters || _viewModel.IsUpdatingFilterValues || (_viewModel.OperationInProgress && !_viewModel.IsAnalyzing)) return;
        var location = listBoxFolders.SelectedItem as LocationInfo;
        locationLabel.Text = location?.Label ?? "";
        locationAccess.Text = "Availability unchecked";
        _viewModel.LocationFilter = location?.Path ?? "";
        _viewModel.FocusFolder = "";
        await _viewModel.ApplyFiltersAsync();
    }
    private async void AllFolders_Click(object sender, RoutedEventArgs e)
    {
        listBoxFolders.SelectedItem = null;
        _viewModel.FocusFolder = _viewModel.LocationFilter = "";
        await _viewModel.ApplyFiltersAsync();
    }
    private void RenameLocation_Click(object sender, RoutedEventArgs e)
    {
        if (listBoxFolders.SelectedItem is LocationInfo location) _viewModel.RenameLocation(location.Path, locationLabel.Text);
    }
    private async void CheckLocation_Click(object sender, RoutedEventArgs e)
    {
        if (listBoxFolders.SelectedItem is not LocationInfo location) return;
        locationAccess.Text = "Checking availability…";
        var available = await Task.Run(() => Directory.Exists(location.Path));
        if (listBoxFolders.SelectedItem == location)
            locationAccess.Text = available ? "Location available · files not reverified" : "Location unavailable · saved evidence remains available";
    }
    private void FolderSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_viewModel == null || _changingWorkspaceFilters || _viewModel.IsUpdatingFilterValues || (_viewModel.OperationInProgress && !_viewModel.IsAnalyzing)) return;
        _searchTimer.Stop(); _searchTimer.Start();
    }
    private async void WorkspaceFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null || _changingWorkspaceFilters || _viewModel.IsUpdatingFilterValues || (_viewModel.OperationInProgress && !_viewModel.IsAnalyzing)) return;
        await _viewModel.ApplyFiltersAsync();
    }
    private async void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        await NavigateToFolderAsync("");
    }
    private async Task NavigateToFolderAsync(string folderPath)
    {
        _searchTimer.Stop();
        _changingWorkspaceFilters = true;
        try
        {
            filterError.Text = "";
            listBoxFolders.SelectedItem = null;
            locationLabel.Text = "";
            locationAccess.Text = "Availability unchecked";
        }
        finally { _changingWorkspaceFilters = false; }
        await _viewModel.ShowFolderComparisonsAsync(folderPath);
    }
    private void Pane_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _viewModel.ComparisonsWidth = comparisonsColumn.ActualWidth;
    }
    private void AnalysisDetails_Click(object sender, RoutedEventArgs e)
    {
        var details = new TextBox { Text = _viewModel.IssuesText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        new Window { Title = "Analysis details", Owner = this, Width = 720, Height = 420, Content = details, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = (sender as Button)?.Tag as string == "A" ? _viewModel.LeftFolderDisplay : _viewModel.RightFolderDisplay;
        try
        {
            if (!Directory.Exists(path)) { _viewModel.StatusMessage = "Location unavailable. You can still inspect saved evidence or copy the path."; return; }
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { path } });
        }
        catch (Exception ex) { _viewModel.StatusMessage = $"Could not open folder: {ex.Message}"; }
    }
    private void CopyPaths_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_viewModel.LeftFolderDisplay + Environment.NewLine + _viewModel.RightFolderDisplay); _viewModel.StatusMessage = "Folder paths copied."; }
        catch (Exception ex) { _viewModel.StatusMessage = $"Could not copy paths: {ex.Message}"; }
    }
    private void ClearSortHeaders(ICollectionView view)
    {
        foreach (var item in _sortHeaders.Where(item => item.Value.View == view)) item.Key.Content = item.Value.Text;
    }
    private void UpdateSortHeader(GridViewColumnHeader header, string text, ICollectionView view, string property)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ClearSortHeaders(view);
        _sortHeaders[header] = (text, view);
        header.Content = text + (view.SortDescriptions.First(s => s.PropertyName == property).Direction == ListSortDirection.Ascending ? " ↑" : " ↓");
    }
}
