// ClutterFlock - MainWindow.cs (Refactored)
// -----------------------------------------------------------------------------
// This is the new simplified UI layer that uses the refactored MVVM architecture.
// All business logic has been moved to the Core layer and ViewModels.
// -----------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ClutterFlock.ViewModels;
using ClutterFlock.Models;

namespace ClutterFlock
{
    public partial class MainWindow : Window
    {
        private static readonly string ProductVersion = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
        private MainViewModel _viewModel;
        private CollectionViewSource _folderMatchesViewSource;
        private CollectionViewSource _fileDetailsViewSource;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            
            // Set window title with version information
            UpdateWindowTitle();
            
            // Set up collection view sources for advanced sorting
            _folderMatchesViewSource = new CollectionViewSource { Source = _viewModel.FilteredFolderMatches };
            _fileDetailsViewSource = new CollectionViewSource { Source = _viewModel.FileDetails };
            
            listViewFolderMatches.ItemsSource = _folderMatchesViewSource.View;
            listViewFiles.ItemsSource = _fileDetailsViewSource.View;
            
            // Bind folder list
            listBoxFolders.ItemsSource = _viewModel.Locations;
            listBoxFolders.SelectionChanged += (_, _) => UpdateButtonStates();
            
            // Set up initial UI state
            UpdateButtonStates();
            
            // Subscribe to ViewModel property changes for UI updates
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Handle window closing to properly dispose resources
            this.Closing += MainWindow_Closing;
            ConfigureWorkspaceCommands();
        }

        private void UpdateWindowTitle()
            => Title = $"{_viewModel.ProjectTitle} — ClutterFlock v{ProductVersion}";

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose && IsVisible)
            {
                e.Cancel = true;
                if (_viewModel.OperationInProgress)
                {
                    _viewModel.CancelOperation();
                    _viewModel.StatusMessage = "Wait for the operation to finish before closing. Cancellable work is stopping.";
                    return;
                }
                if (_closePending) return;
                _closePending = true;
                // Finish WPF's current Closing event before showing a dialog or closing again.
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        if (!await ConfirmReplaceAsync()) return;
                        _allowClose = true;
                        Close();
                    }
                    catch (Exception ex) { _viewModel.StatusMessage = $"Could not close workspace: {ex.Message}"; }
                    finally { _closePending = false; }
                }));
                return;
            }
            // Cancel any ongoing operations
            // Unsubscribe from events to prevent memory leaks
            _viewModel.CancelOperation();
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _searchTimer.Stop();
            _viewModel.Dispose();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Ensure UI updates happen on the UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => ViewModel_PropertyChanged(sender, e));
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(MainViewModel.OperationInProgress):
                case nameof(MainViewModel.IsPopulatingResults):
                    if (!_viewModel.IsPopulatingResults)
                    {
                        listViewFolderMatches.SelectedItem = _viewModel.SelectedFolderMatch;
                    }
                    UpdateButtonStates();
                    break;
                case nameof(MainViewModel.CanRemoveFolders):
                    UpdateButtonStates();
                    break;
                case nameof(MainViewModel.MinimumSimilarity):
                    txtMinSimilarity.Text = _viewModel.MinimumSimilarity.ToString();
                    break;
                case nameof(MainViewModel.MinimumSizeMB):
                    txtMinSize.Text = _viewModel.MinimumSizeMB.ToString();
                    break;
                case nameof(MainViewModel.ProjectTitle):
                    UpdateWindowTitle();
                    break;
                case nameof(MainViewModel.ProjectPath):
                    comparisonsColumn.Width = new GridLength(Math.Clamp(_viewModel.ComparisonsWidth, 380, Math.Max(380, ActualWidth - 430)));
                    break;
                case nameof(MainViewModel.SelectedFolderMatch):
                    listViewFolderMatches.SelectedItem = _viewModel.SelectedFolderMatch;
                    break;
                case nameof(MainViewModel.StatusMessage):
                    if (statusLabel != null)
                        statusLabel.Text = _viewModel.StatusMessage;
                    break;
                case nameof(MainViewModel.CurrentProgress):
                    if (mainProgressBar != null)
                        mainProgressBar.Value = Math.Max(0, Math.Min(_viewModel.CurrentProgress, _viewModel.MaxProgress));
                    break;
                case nameof(MainViewModel.MaxProgress):
                    if (mainProgressBar != null)
                        mainProgressBar.Maximum = Math.Max(1, _viewModel.MaxProgress);
                    break;
                case nameof(MainViewModel.IsProgressIndeterminate):
                    if (mainProgressBar != null)
                        mainProgressBar.IsIndeterminate = _viewModel.IsProgressIndeterminate;
                    break;
                case nameof(MainViewModel.LeftFolderDisplay):
                    if (txtLeftFolderDisplay != null)
                        txtLeftFolderDisplay.Text = _viewModel.LeftFolderDisplay;
                    break;
                case nameof(MainViewModel.RightFolderDisplay):
                    if (txtRightFolderDisplay != null)
                        txtRightFolderDisplay.Text = _viewModel.RightFolderDisplay;
                    break;
                case nameof(MainViewModel.FileCountDisplay):
                    if (lblFileCount != null)
                        lblFileCount.Text = _viewModel.FileCountDisplay;
                    break;
            }
        }

        private void UpdateButtonStates()
        {
            bool hasFolders = _viewModel.ScanFolders.Count > 0;
            bool hasSelection = listBoxFolders.SelectedItem != null;
            
            btnAddFolder.IsEnabled = _viewModel.CanAddFolders;
            btnRemoveFolder.IsEnabled = hasSelection && _viewModel.CanRemoveFolders;
            btnRunComparison.IsEnabled = _viewModel.CanRunComparison;
            btnSaveProject.IsEnabled = _viewModel.CanSaveProject;
            btnLoadProject.IsEnabled = _viewModel.CanLoadProject;
            btnApplyFilters.IsEnabled = _viewModel.CanApplyFilters;
            btnClearSort.IsEnabled = _viewModel.CanApplyFilters;
            btnCancel.IsEnabled = _viewModel.CanCancel;
            btnCancel.Visibility = _viewModel.CanCancel ? Visibility.Visible : Visibility.Collapsed;

        }

        // ═════════════════════════════ EVENT HANDLERS ═════════════════════════════
        
        private async void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Add locations to compare", Multiselect = true };
            if (dlg.ShowDialog(this) != true) return;
            foreach (var path in dlg.FolderNames) await _viewModel.AddFolderAsync(path);
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (listBoxFolders.SelectedItem is not LocationInfo location) return;
            _viewModel.RemoveFolder(location.Path);
        }

        private async void RunComparison_Click(object sender, RoutedEventArgs e)
        {
            await _viewModel.RunComparisonAsync();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelOperation();
        }

        private async void SaveProject_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync(false);

        private async void LoadProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ClutterFlock Projects (*.cfp;*.dfp)|*.cfp;*.dfp|All Files (*.*)|*.*",
                DefaultExt = "cfp"
            };
            if (dlg.ShowDialog(this) != true || !await ConfirmReplaceAsync()) return;
            await _viewModel.LoadProjectAsync(dlg.FileName);
        }

        private async void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            // Update ViewModel filter properties from UI with validation
            if (!double.TryParse(txtMinSimilarity.Text, out var similarity) || !double.IsFinite(similarity) || similarity is < 0 or > 100 ||
                !double.TryParse(txtMinSize.Text, out var size) || !double.IsFinite(size) || size < 0 || size >= long.MaxValue / (1024.0 * 1024))
            {
                filterError.Text = "Enter similarity from 0 to 100 and a nonnegative folder size.";
                return;
            }
            filterError.Text = "";
            _viewModel.MinimumSimilarity = similarity;
            _viewModel.MinimumSizeMB = size;
            await _viewModel.ApplyFiltersAsync();
        }

        private void listViewFolderMatches_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null || _viewModel.IsPopulatingResults) return;
            if (listViewFolderMatches.SelectedItem is FolderMatch selectedMatch)
            {
                _viewModel.SelectedFolderMatch = selectedMatch;
            }
            else
            {
                _viewModel.SelectedFolderMatch = null;
            }
        }

        private void ClearSort_Click(object sender, RoutedEventArgs e)
        {
            _folderMatchesViewSource.View.SortDescriptions.Clear();
            ClearSortHeaders(_folderMatchesViewSource.View);
            _viewModel.StatusMessage = "Sorting cleared - showing default order.";
        }

        private void ClearFileSort_Click(object sender, RoutedEventArgs e)
        {
            _fileDetailsViewSource.View.SortDescriptions.Clear();
            ClearSortHeaders(_fileDetailsViewSource.View);
            _viewModel.StatusMessage = "File sorting cleared - showing default order.";
        }

        // ═════════════════════════════ SORTING HANDLERS ═════════════════════════════
        
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (listViewFolderMatches.ItemsSource == null) return;
            
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header?.Content?.ToString() is not string text) return;
            var headerText = (header.Tag as string) ?? text;
            header.Tag = headerText;

            string property = headerText switch
            {
                "Similarity (%)" => "SimilarityPercentage",
                "Similarity %" => "SimilarityPercentage",
                "Overlap %" => "SimilarityPercentage",
                "Size" => "FolderSizeBytes",
                "Folder pair" => "LeftFolder",
                "Master Folder" => "LeftFolder",
                _ => headerText
            };

            ApplySorting(_folderMatchesViewSource.View, property);
            UpdateSortHeader(header!, headerText, _folderMatchesViewSource.View, property);
        }

        private void FileListGridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (listViewFiles.ItemsSource == null) return;
            
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header?.Content?.ToString() is not string text) return;
            var headerText = (header.Tag as string) ?? text;
            header.Tag = headerText;

            string property = headerText switch
            {
                "← File Name" => "LeftFileName",
                "← Size" => "LeftSizeBytes",
                "A size" => "LeftSizeBytes",
                "B size" => "RightSizeBytes",
                "A modified" => "LeftDate",
                "B modified" => "RightDate",
                "File" => "PrimaryFileName",
                "Status" => "Status",
                "→ File Name" => "RightFileName",
                "→ Size" => "RightSizeBytes",
                _ => headerText
            };

            ApplySorting(_fileDetailsViewSource.View, property);
            UpdateSortHeader(header!, headerText, _fileDetailsViewSource.View, property);
        }

        private void ApplySorting(ICollectionView view, string propertyName)
        {
            var direction = ListSortDirection.Ascending;
            
            // Check if already sorted by this property
            var existingSort = view.SortDescriptions.FirstOrDefault(sd => sd.PropertyName == propertyName);
            if (existingSort.PropertyName == propertyName)
            {
                direction = existingSort.Direction == ListSortDirection.Ascending 
                    ? ListSortDirection.Descending 
                    : ListSortDirection.Ascending;
                view.SortDescriptions.Remove(existingSort);
            }
            if (!System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            {
                // Single column sort - clear existing sorts unless Ctrl is held
                view.SortDescriptions.Clear();
            }

            view.SortDescriptions.Add(new SortDescription(propertyName, direction));
            view.Refresh();
        }
    }
}
