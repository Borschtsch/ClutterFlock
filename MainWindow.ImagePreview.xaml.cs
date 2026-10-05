using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClutterFlock.Core;
using ClutterFlock.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListViewItem = System.Windows.Controls.ListViewItem;

namespace ClutterFlock;

public partial class MainWindow
{
    private ImageComparisonWindow? _imageComparisonWindow;

    private void UpdateImagePreview(FileDetailInfo? row)
    {
        if (imagePreview == null) return;
        var supported = row != null && (ImagePreviewLoader.IsImage(row.LeftFullPath) || ImagePreviewLoader.IsImage(row.RightFullPath));
        imagePreviewPanel.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        imagePreviewRow.Height = supported ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
        if (supported) imagePreview.ShowFiles(row!.LeftFullPath, row.RightFullPath, 480);
        else imagePreview.Clear();
    }

    private void ImagePreview_Open(object sender, RoutedEventArgs e) => OpenImageComparison();

    private void FileRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: FileDetailInfo row } && ReferenceEquals(row, listViewFiles.SelectedItem))
        { OpenImageComparison(); e.Handled = true; }
    }

    private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenImageComparison(); e.Handled = true; }
    }

    private void OpenImageComparison()
    {
        if (_imageComparisonWindow?.IsActionPending == true) return;
        if (listViewFiles.SelectedItem is not FileDetailInfo row ||
            (!ImagePreviewLoader.IsImage(row.LeftFullPath) && !ImagePreviewLoader.IsImage(row.RightFullPath))) return;
        _imageComparisonWindow?.Close();
        var pair = _viewModel.SelectedFolderMatch;
        bool SamePair() => pair != null && _viewModel.SelectedFolderMatch is { } current
            && pair.LeftFolder == current.LeftFolder && pair.RightFolder == current.RightFolder;
        var rows = _viewModel.GetFileComparisonSnapshot();
        ImageComparisonWindow window = null!;
        window = new ImageComparisonWindow(row, rows.Count > 0 ? rows : _viewModel.FileDetails,
            async (action, current) =>
            {
                var path = action.EndsWith('A') ? current.LeftFullPath : current.RightFullPath;
                if (action.StartsWith("Open")) { OpenItem(path); return; }
                if (!SamePair() || !_viewModel.CanManageFolders) return;
                if (action.StartsWith("Merge"))
                {
                    var plan = await _viewModel.PrepareFileMergeAsync(current, action.EndsWith('A'));
                    if (plan != null)
                    {
                        var confirmation = new ActionConfirmationWindow(action.EndsWith('A') ? "Merge displayed file to A" : "Merge displayed file to B",
                            plan.Description, "Merge this file only", (Style)FindResource("DangerButton")) { Owner = window };
                        if (confirmation.ShowDialog() == true)
                        {
                            await _viewModel.ExecuteFileMergeAsync(plan);
                            window.RefreshFiles(SamePair() ? _viewModel.GetFileComparisonSnapshot() : Array.Empty<FileDetailInfo>());
                        }
                    }
                }
                else if (await ConfirmDeleteFileAsync(path, window))
                    window.RefreshFiles(SamePair() ? _viewModel.GetFileComparisonSnapshot() : Array.Empty<FileDetailInfo>());
                window.SetStatus(_viewModel.StatusMessage);
            }, () => SamePair() && _viewModel.CanManageFolders, (Style)FindResource("DangerButton")) { Owner = this };
        System.ComponentModel.PropertyChangedEventHandler changed = (_, e) =>
        {
            if (!window.IsActionPending && e.PropertyName == nameof(ViewModels.MainViewModel.SelectedFolderMatch) && !SamePair())
                window.Close();
            else if (e.PropertyName == nameof(ViewModels.MainViewModel.CanManageFolders)) window.RefreshControls();
        };
        _viewModel.PropertyChanged += changed;
        window.Closed += (_, _) => _viewModel.PropertyChanged -= changed;
        _imageComparisonWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_imageComparisonWindow, window)) _imageComparisonWindow = null; };
        window.Show();
    }
}
