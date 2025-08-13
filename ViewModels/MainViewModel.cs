using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.Services;

namespace ClutterFlock.ViewModels
{
    /// <summary>
    /// Main ViewModel for the application, implementing MVVM pattern
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        #region Private Fields
        private readonly ICacheManager _cacheManager;
        private readonly IFolderScanner _folderScanner;
        private readonly IDuplicateAnalyzer _duplicateAnalyzer;
        private readonly IProjectManager _projectManager;
        private readonly IFileComparer _fileComparer;
        
        private readonly List<string> _scanFolders = new();
        private List<FolderMatch> _allFolderMatches = new();
        private List<FileDetailInfo> _allFileDetails = new();
        
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _operationInProgress;
        private bool _isPopulatingResults;
        private bool _showUniqueFiles;
        
        private string _statusMessage = "Ready to scan";
        private int _currentProgress;
        private int _maxProgress = 1;
        private bool _isProgressIndeterminate;
        
        private FolderMatch? _selectedFolderMatch;
        private string _leftFolderDisplay = string.Empty;
        private string _rightFolderDisplay = string.Empty;
        private string _fileCountDisplay = "0";
        
        private double _minimumSimilarity = 50.0;
        private double _minimumSizeMB = 1.0;
        #endregion

        #region Public Properties
        public ObservableCollection<string> ScanFolders { get; } = new();
        public ObservableCollection<FolderMatch> FilteredFolderMatches { get; } = new();
        public ObservableCollection<FileDetailInfo> FileDetails { get; } = new();

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int CurrentProgress
        {
            get => _currentProgress;
            set => SetProperty(ref _currentProgress, value);
        }

        public int MaxProgress
        {
            get => _maxProgress;
            set => SetProperty(ref _maxProgress, value);
        }

        public bool IsProgressIndeterminate
        {
            get => _isProgressIndeterminate;
            set => SetProperty(ref _isProgressIndeterminate, value);
        }

        public bool OperationInProgress
        {
            get => _operationInProgress;
            set
            {
                SetProperty(ref _operationInProgress, value);
                OnPropertyChanged(nameof(CanAddFolders));
                OnPropertyChanged(nameof(CanRemoveFolders));
                OnPropertyChanged(nameof(CanRunComparison));
                OnPropertyChanged(nameof(CanSaveProject));
                OnPropertyChanged(nameof(CanLoadProject));
                OnPropertyChanged(nameof(CanApplyFilters));
                OnPropertyChanged(nameof(CanCancel));
            }
        }

        public bool IsPopulatingResults
        {
            get => _isPopulatingResults;
            set
            {
                SetProperty(ref _isPopulatingResults, value);
                OnPropertyChanged(nameof(CanApplyFilters));
            }
        }

        public FolderMatch? SelectedFolderMatch
        {
            get => _selectedFolderMatch;
            set
            {
                SetProperty(ref _selectedFolderMatch, value);
                _ = UpdateFileDetailsAsync();
            }
        }

        public bool ShowUniqueFiles
        {
            get => _showUniqueFiles;
            set
            {
                SetProperty(ref _showUniqueFiles, value);
                FilterFileDetails();
            }
        }

        public string LeftFolderDisplay
        {
            get => _leftFolderDisplay;
            set => SetProperty(ref _leftFolderDisplay, value);
        }

        public string RightFolderDisplay
        {
            get => _rightFolderDisplay;
            set => SetProperty(ref _rightFolderDisplay, value);
        }

        public string FileCountDisplay
        {
            get => _fileCountDisplay;
            set => SetProperty(ref _fileCountDisplay, value);
        }

        public double MinimumSimilarity
        {
            get => _minimumSimilarity;
            set => SetProperty(ref _minimumSimilarity, value);
        }

        public double MinimumSizeMB
        {
            get => _minimumSizeMB;
            set => SetProperty(ref _minimumSizeMB, value);
        }

        // Command availability properties
        public bool CanAddFolders => !OperationInProgress;
        public bool CanRemoveFolders => !OperationInProgress && ScanFolders.Count > 0;
        public bool CanRunComparison => !OperationInProgress && ScanFolders.Count > 0;
        public bool CanSaveProject => !OperationInProgress && ScanFolders.Count > 0;
        public bool CanLoadProject => !OperationInProgress;
        public bool CanApplyFilters => !OperationInProgress && !IsPopulatingResults && _allFolderMatches.Count > 0;
        public bool CanCancel => OperationInProgress;
        #endregion

        #region Constructor
        public MainViewModel()
        {
            _cacheManager = new CacheManager();
            var errorRecoveryService = new ErrorRecoveryService();
            _folderScanner = new FolderScanner(_cacheManager, errorRecoveryService);
            _duplicateAnalyzer = new DuplicateAnalyzer(_cacheManager, errorRecoveryService);
            _projectManager = new ProjectManager();
            _fileComparer = new FileComparer();
        }
        #endregion

        #region Public Methods
        public async Task<bool> AddFolderAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || _scanFolders.Contains(folderPath))
                return false;

            try
            {
                // Immediately show that operation started
                StatusMessage = "Starting folder analysis...";
                OperationInProgress = true;
                CurrentProgress = 0;
                MaxProgress = 100;
                IsProgressIndeterminate = true;
                
                _cancellationTokenSource = new CancellationTokenSource();

                var progress = new Progress<AnalysisProgress>(UpdateProgress);
                
                // Add a timeout to prevent hanging indefinitely
                using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMinutes(30)); // 30 minute timeout
                using var combinedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _cancellationTokenSource.Token, 
                    timeoutSource.Token);

                var subfolders = await _folderScanner.ScanFolderHierarchyAsync(folderPath, progress, combinedSource.Token);

                _scanFolders.Add(folderPath);
                ScanFolders.Add(folderPath);
                
                StatusMessage = $"Added folder with {subfolders.Count} subfolders";
                ResetProgress();
                return true;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled or timed out";
                ResetProgress();
                return false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding folder: {ex.Message}";
                ResetProgress();
                return false;
            }
            finally
            {
                OperationInProgress = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public void RemoveFolder(string folderPath)
        {
            if (!_scanFolders.Contains(folderPath)) return;

            _scanFolders.Remove(folderPath);
            ScanFolders.Remove(folderPath);
            _cacheManager.RemoveFolderFromCache(folderPath);
            
            StatusMessage = $"Removed folder: {Path.GetFileName(folderPath)}";
        }

        public async Task<bool> RunComparisonAsync()
        {
            if (_scanFolders.Count < 1) return false;

            try
            {
                OperationInProgress = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // Show immediate progress feedback
                StatusMessage = "Starting comparison analysis...";
                CurrentProgress = 0;
                MaxProgress = 100;
                IsProgressIndeterminate = true;

                var progress = new Progress<AnalysisProgress>(UpdateProgress);
                
                // Run the heavy work on a background thread to avoid UI blocking
                var result = await Task.Run(async () =>
                {
                    try
                    {
                        // Get all cached folders
                        var allFolders = _cacheManager.GetAllFolderFiles().Keys
                            .Where(folder => _scanFolders.Any(root => folder.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                            .ToList();

                        if (allFolders.Count < 2)
                        {
                            return (success: false, message: "Need at least 2 folders to compare", matches: new List<FolderMatch>());
                        }

                        // Find duplicate files
                        var fileMatches = await _duplicateAnalyzer.FindDuplicateFilesAsync(allFolders, progress, _cancellationTokenSource.Token);
                        
                        // Aggregate into folder matches
                        var folderMatches = await _duplicateAnalyzer.AggregateFolderMatchesAsync(fileMatches, _cacheManager, progress);
                        
                        return (success: true, message: $"Analysis complete: {folderMatches.Count} folder matches found", matches: folderMatches);
                    }
                    catch (OperationCanceledException)
                    {
                        return (success: false, message: "Comparison cancelled", matches: new List<FolderMatch>());
                    }
                    catch (Exception ex)
                    {
                        return (success: false, message: $"Error during comparison: {ex.Message}", matches: new List<FolderMatch>());
                    }
                }, _cancellationTokenSource.Token);

                // Update results on UI thread
                _allFolderMatches = result.matches;
                StatusMessage = result.message;
                
                if (result.success)
                {
                    // Apply filters and populate results
                    ApplyFilters();
                    ResetProgress();
                    return true;
                }
                else
                {
                    ResetProgress();
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Comparison cancelled";
                ResetProgress();
                return false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during comparison: {ex.Message}";
                ResetProgress();
                return false;
            }
            finally
            {
                OperationInProgress = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public async void ApplyFilters()
        {
            try
            {
                IsPopulatingResults = true;
                
                // Show progress for filter application
                CurrentProgress = 0;
                MaxProgress = 100;
                IsProgressIndeterminate = false;
                
                // Run filtering on background thread
                var filteredMatches = await Task.Run(() =>
                {
                    var criteria = new FilterCriteria
                    {
                        MinimumSimilarityPercent = MinimumSimilarity,
                        MinimumSizeBytes = (long)(MinimumSizeMB * 1024 * 1024)
                    };

                    return _duplicateAnalyzer.ApplyFilters(_allFolderMatches, criteria);
                });
                
                // Clear existing results on UI thread
                FilteredFolderMatches.Clear();
                
                // Add results in batches to keep UI responsive
                const int batchSize = 50;
                for (int i = 0; i < filteredMatches.Count; i += batchSize)
                {
                    var batch = filteredMatches.Skip(i).Take(batchSize);
                    foreach (var match in batch)
                    {
                        FilteredFolderMatches.Add(match);
                    }
                    
                    // Update progress
                    CurrentProgress = filteredMatches.Count > 0 ? (int)((double)(i + batchSize) / filteredMatches.Count * 100) : 100;
                    StatusMessage = $"Populating results: {Math.Min(i + batchSize, filteredMatches.Count):N0} of {filteredMatches.Count:N0} groups";
                    
                    // Allow UI to update - use Dispatcher.Yield for WPF
                    await System.Windows.Threading.Dispatcher.Yield();
                }

                StatusMessage = $"Filter applied: showing {filteredMatches.Count:N0} groups";
                ResetProgress();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error applying filters: {ex.Message}";
                ResetProgress();
            }
            finally
            {
                IsPopulatingResults = false;
            }
        }

        public void CancelOperation()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling operation...";
        }

        public async Task<bool> SaveProjectAsync(string filePath)
        {
            try
            {
                var projectData = _cacheManager.ExportToProjectData(_scanFolders);
                await _projectManager.SaveProjectAsync(filePath, projectData);
                StatusMessage = "Project saved successfully";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving project: {ex.Message}";
                return false;
            }
        }

        public async Task<bool> LoadProjectAsync(string filePath)
        {
            try
            {
                var projectData = await _projectManager.LoadProjectAsync(filePath);
                _cacheManager.LoadFromProjectData(projectData);
                
                // Clear existing results
                _allFolderMatches.Clear();
                FilteredFolderMatches.Clear();
                _allFileDetails.Clear();
                FileDetails.Clear();
                ClearFileDetails();
                
                _scanFolders.Clear();
                ScanFolders.Clear();
                
                foreach (var folder in projectData.ScanFolders)
                {
                    // Validate that folder still exists
                    if (Directory.Exists(folder))
                    {
                        _scanFolders.Add(folder);
                        ScanFolders.Add(folder);
                    }
                    else
                    {
                        StatusMessage = $"Warning: Folder no longer exists: {folder}";
                    }
                }

                StatusMessage = $"Project loaded successfully - {ScanFolders.Count} folders available";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading project: {ex.Message}";
                return false;
            }
        }
        #endregion

        #region Private Methods
        private void UpdateProgress(AnalysisProgress progress)
        {
            // Ensure UI updates happen on the UI thread
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => UpdateProgress(progress));
                return;
            }

            StatusMessage = progress.StatusMessage;
            CurrentProgress = progress.CurrentProgress;
            MaxProgress = Math.Max(1, progress.MaxProgress); // Ensure MaxProgress is never 0
            IsProgressIndeterminate = progress.IsIndeterminate;
        }

        private void ResetProgress()
        {
            // Ensure UI updates happen on the UI thread
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(ResetProgress);
                return;
            }

            CurrentProgress = 0;
            MaxProgress = 1;
            IsProgressIndeterminate = false;
        }

        private async Task UpdateFileDetailsAsync()
        {
            if (SelectedFolderMatch == null)
            {
                ClearFileDetails();
                return;
            }

            try
            {
                var leftFolder = SelectedFolderMatch.LeftFolder;
                var rightFolder = SelectedFolderMatch.RightFolder;
                var duplicateFiles = SelectedFolderMatch.DuplicateFiles;

                // Validate folders still exist
                if (!Directory.Exists(leftFolder) || !Directory.Exists(rightFolder))
                {
                    StatusMessage = "One or both selected folders no longer exist";
                    ClearFileDetails();
                    return;
                }

                // Update folder displays
                var leftInfo = _cacheManager.GetFolderInfo(leftFolder);
                var rightInfo = _cacheManager.GetFolderInfo(rightFolder);
                
                LeftFolderDisplay = leftInfo?.LatestModificationDate != null
                    ? $"{leftFolder} ({leftInfo.LatestModificationDate:yyyy-MM-dd HH:mm})"
                    : leftFolder;
                    
                RightFolderDisplay = rightInfo?.LatestModificationDate != null
                    ? $"{rightFolder} ({rightInfo.LatestModificationDate:yyyy-MM-dd HH:mm})"
                    : rightFolder;

                // Build file comparison on background thread
                var fileDetails = await Task.Run(() =>
                {
                    try
                    {
                        return _fileComparer.BuildFileComparison(leftFolder, rightFolder, duplicateFiles, _cacheManager);
                    }
                    catch (Exception ex)
                    {
                        // Log the error but don't crash the UI
                        System.Diagnostics.Debug.WriteLine($"Error building file comparison: {ex.Message}");
                        return new List<FileDetailInfo>();
                    }
                });

                _allFileDetails = fileDetails;
                FilterFileDetails();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error updating file details: {ex.Message}";
                ClearFileDetails();
            }
        }

        private void FilterFileDetails()
        {
            FileDetails.Clear();
            
            var filteredFiles = _fileComparer.FilterFileDetails(_allFileDetails, ShowUniqueFiles);
            foreach (var file in filteredFiles)
            {
                FileDetails.Add(file);
            }

            var duplicateCount = _allFileDetails.Count(f => f.IsDuplicate);
            var uniqueCount = _allFileDetails.Count(f => !f.IsDuplicate);

            FileCountDisplay = ShowUniqueFiles
                ? $"{_allFileDetails.Count} total ({duplicateCount} duplicates, {uniqueCount} unique)"
                : $"{duplicateCount} duplicates";
        }

        private void ClearFileDetails()
        {
            LeftFolderDisplay = string.Empty;
            RightFolderDisplay = string.Empty;
            FileCountDisplay = "0";
            _allFileDetails.Clear();
            FileDetails.Clear();
        }
        #endregion

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion

        #region IDisposable Implementation
        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cancel any ongoing operations
                    CancelOperation();
                    
                    // Dispose cancellation token source
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                }
                _disposed = true;
            }
        }
        #endregion
    }
}
