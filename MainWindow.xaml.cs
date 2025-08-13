// ClutterFlock - MainWindow.cs (Refactored)
// -----------------------------------------------------------------------------
// This is the new simplified UI layer that uses the refactored MVVM architecture.
// All business logic has been moved to the Core layer and ViewModels.
// -----------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ClutterFlock.ViewModels;
using ClutterFlock.Models;

namespace ClutterFlock
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        private CollectionViewSource _folderMatchesViewSource;
        private CollectionViewSource _fileDetailsViewSource;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            
            // Set window title with version information
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            this.Title = $"ClutterFlock v{version?.Major}.{version?.Minor}.{version?.Build}";
            
            // Set up collection view sources for advanced sorting
            _folderMatchesViewSource = new CollectionViewSource { Source = _viewModel.FilteredFolderMatches };
            _fileDetailsViewSource = new CollectionViewSource { Source = _viewModel.FileDetails };
            
            listViewFolderMatches.ItemsSource = _folderMatchesViewSource.View;
            listViewFiles.ItemsSource = _fileDetailsViewSource.View;
            
            // Bind folder list
            listBoxFolders.ItemsSource = _viewModel.ScanFolders;
            
            // Set up initial UI state
            UpdateButtonStates();
            
            // Subscribe to ViewModel property changes for UI updates
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Handle window closing to properly dispose resources
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cancel any ongoing operations
            _viewModel?.CancelOperation();
            
            // Unsubscribe from events to prevent memory leaks
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.Dispose();
            }
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
                    UpdateButtonStates();
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
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Add folder to scan" };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            
            string path = dlg.SelectedPath;
            if (_viewModel.ScanFolders.Contains(path))
            {
                System.Windows.MessageBox.Show("Folder already in list.");
                return;
            }

            var success = await _viewModel.AddFolderAsync(path);
            if (!success)
            {
                System.Windows.MessageBox.Show("Failed to add folder. Check the status message for details.");
            }
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (listBoxFolders.SelectedItem is not string path) return;
            _viewModel.RemoveFolder(path);
        }

        private async void RunComparison_Click(object sender, RoutedEventArgs e)
        {
            var success = await _viewModel.RunComparisonAsync();
            if (!success)
            {
                System.Windows.MessageBox.Show("Comparison failed. Check the status message for details.");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CancelOperation();
        }

        private async void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "ClutterFlock Project (*.cfp)|*.cfp",
                DefaultExt = "cfp"
            };
            if (dlg.ShowDialog() != true) return;

            var success = await _viewModel.SaveProjectAsync(dlg.FileName);
            if (success)
            {
                System.Windows.MessageBox.Show("Project saved successfully.");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to save project. Check the status message for details.");
            }
        }

        private async void LoadProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ClutterFlock Project (*.cfp)|*.cfp|All Files (*.*)|*.*",
                DefaultExt = "cfp"
            };
            if (dlg.ShowDialog() != true) return;

            var success = await _viewModel.LoadProjectAsync(dlg.FileName);
            if (success)
            {
                System.Windows.MessageBox.Show("Project loaded successfully.");
            }
            else
            {
                System.Windows.MessageBox.Show("Failed to load project. Check the status message for details.");
            }
        }

        private void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            // Update ViewModel filter properties from UI with validation
            if (double.TryParse(txtMinSimilarity?.Text, out var similarity))
            {
                _viewModel.MinimumSimilarity = Math.Max(0, Math.Min(100, similarity));
            }
            
            if (double.TryParse(txtMinSize?.Text, out var sizeMB))
            {
                _viewModel.MinimumSizeMB = Math.Max(0, sizeMB);
            }
                
            _viewModel.ApplyFilters();
        }

        private void listViewFolderMatches_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (listViewFolderMatches.SelectedItem is FolderMatch selectedMatch)
            {
                _viewModel.SelectedFolderMatch = selectedMatch;
            }
            else
            {
                _viewModel.SelectedFolderMatch = null;
            }
        }

        private void ShowUniqueFiles_Changed(object sender, RoutedEventArgs e)
        {
            _viewModel.ShowUniqueFiles = chkShowUniqueFiles.IsChecked == true;
        }

        private void ClearSort_Click(object sender, RoutedEventArgs e)
        {
            _folderMatchesViewSource.View.SortDescriptions.Clear();
            _viewModel.StatusMessage = "Sorting cleared - showing default order.";
        }

        private void ClearFileSort_Click(object sender, RoutedEventArgs e)
        {
            _fileDetailsViewSource.View.SortDescriptions.Clear();
            _viewModel.StatusMessage = "File sorting cleared - showing default order.";
        }

        // ═════════════════════════════ SORTING HANDLERS ═════════════════════════════
        
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (listViewFolderMatches.ItemsSource == null) return;
            
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header?.Content?.ToString() is not string headerText) return;

            string property = headerText switch
            {
                "Similarity (%)" => "SimilarityPercentage",
                "Similarity %" => "SimilarityPercentage",
                "Size" => "SizeDisplay",
                "Master Folder" => "LeftFolder",
                _ => headerText
            };

            ApplySorting(_folderMatchesViewSource.View, property);
        }

        private void FileListGridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (listViewFiles.ItemsSource == null) return;
            
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header?.Content?.ToString() is not string headerText) return;

            string property = headerText switch
            {
                "← File Name" => "LeftFileName",
                "← Size" => "LeftSizeDisplay",
                "→ File Name" => "RightFileName",
                "→ Size" => "RightSizeDisplay",
                _ => headerText
            };

            ApplySorting(_fileDetailsViewSource.View, property);
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
            else if (!System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
            {
                // Single column sort - clear existing sorts unless Ctrl is held
                view.SortDescriptions.Clear();
            }

            view.SortDescriptions.Add(new SortDescription(propertyName, direction));
            view.Refresh();
        }
    }
}
