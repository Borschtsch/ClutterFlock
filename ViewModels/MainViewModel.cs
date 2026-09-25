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
        private ICacheManager _cacheManager;
        private IFolderScanner _folderScanner;
        private IDuplicateAnalyzer _duplicateAnalyzer;
        private readonly IProjectManager _projectManager;
        private readonly IFileComparer _fileComparer;
        private readonly IErrorRecoveryService _errorRecoveryService;
        private bool _hasAnalysis;
        private DateTime _createdDate = DateTime.Now;
        private int _detailVersion;
        private int _filterVersion;
        private int _operationVersion;
        
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
                NotifyAvailability();
            }
        }

        public FolderMatch? SelectedFolderMatch
        {
            get => _selectedFolderMatch;
            set
            {
                if (SetProperty(ref _selectedFolderMatch, value))
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
        public bool CanAddFolders => !OperationInProgress && !IsPopulatingResults && !_disposed;
        public bool CanRemoveFolders => !OperationInProgress && !IsPopulatingResults && !_disposed && ScanFolders.Count > 0;
        public bool CanRunComparison => !OperationInProgress && !IsPopulatingResults && !_disposed && ScanFolders.Count > 0;
        public bool CanSaveProject => !OperationInProgress && !IsPopulatingResults && !_disposed && ScanFolders.Count > 0;
        public bool CanLoadProject => !OperationInProgress && !IsPopulatingResults && !_disposed;
        public bool CanApplyFilters => !OperationInProgress && !IsPopulatingResults && !_disposed && _hasAnalysis;
        public bool CanCancel => OperationInProgress && _cancellationTokenSource != null;
        #endregion

        #region Constructor
        public MainViewModel()
        {
            _cacheManager = new CacheManager();
            _errorRecoveryService = new ErrorRecoveryService();
            _folderScanner = new FolderScanner(_cacheManager, _errorRecoveryService);
            _duplicateAnalyzer = new DuplicateAnalyzer(_cacheManager, _errorRecoveryService);
            _projectManager = new ProjectManager();
            _fileComparer = new FileComparer();
        }
        #endregion

        #region Public Methods
        public async Task<bool> AddFolderAsync(string folderPath)
        {
            if (!CanAddFolders || string.IsNullOrWhiteSpace(folderPath)) return false;
            try
            {
                folderPath = PathUtilities.Normalize(folderPath);
                if (_scanFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase)) return false;
                // Immediately show that operation started.
                // Keep long scans cancellable without the former fixed 30-minute timeout.
                var token = BeginOperation("Scanning folder...");
                var progress = CreateProgress();
                var staged = new CacheManager();
                staged.LoadFromProjectData(_cacheManager.ExportToProjectData(_scanFolders));
                var scanner = new FolderScanner(staged, _errorRecoveryService);
                var folders = await Task.Run(() => scanner.ScanFolderHierarchyAsync(folderPath, progress, token), token);
                token.ThrowIfCancellationRequested();
                SetCache(staged);
                _scanFolders.Add(folderPath);
                ScanFolders.Add(folderPath);
                ClearAnalysis();
                StatusMessage = $"Added root with {folders.Count} folders" + ErrorMessage();
                return true;
            }
            catch (OperationCanceledException) { StatusMessage = "Folder scan cancelled"; return false; }
            catch (Exception ex) { StatusMessage = $"Error adding folder: {ex.Message}"; return false; }
            finally { EndOperation(); }
        }

        public void RemoveFolder(string folderPath)
        {
            if (!CanRemoveFolders) return;
            folderPath = PathUtilities.Normalize(folderPath);
            var existing = _scanFolders.FirstOrDefault(p => p.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;
            _scanFolders.Remove(existing);
            ScanFolders.Remove(existing);
            // A child root remains valid when its parent is removed, and siblings
            // such as Photos and Photos-old must never be confused.
            _cacheManager.RetainFolders(_scanFolders);
            ClearAnalysis();
            NotifyAvailability();
            StatusMessage = $"Removed root: {folderPath}";
        }

        public async Task<bool> RunComparisonAsync()
        {
            if (!CanRunComparison) return false;
            try
            {
                // Show immediate progress feedback.
                var token = BeginOperation("Refreshing folders and comparing files...");
                _ = CurrentFilters(); // Reject invalid filters before replacing any analysis state.
                var roots = _scanFolders.ToList();
                var progress = CreateProgress();
                // Run the heavy work on a background thread to avoid UI blocking.
                var result = await Task.Run(async () =>
                {
                    // Keep the previous snapshot intact until a complete fresh
                    // analysis is available. Never reuse saved hashes as live truth.
                    // Validate that each root still exists before refreshing the snapshot.
                    var missing = roots.Where(root => !Directory.Exists(root)).ToList();
                    if (missing.Count > 0)
                        throw new IOException($"Reconnect the unavailable roots before comparing: {string.Join(", ", missing)}");
                    var cache = new CacheManager();
                    var scanner = new FolderScanner(cache, _errorRecoveryService);
                    foreach (var root in roots)
                        await scanner.ScanFolderHierarchyAsync(root, progress, token);
                    var analyzer = new DuplicateAnalyzer(cache, _errorRecoveryService);
                    // Get all cached folders from this fresh scan and find duplicate files.
                    var files = await analyzer.FindDuplicateFilesAsync(cache.GetAllFolderInfo().Keys.ToList(), progress, token);
                    // Aggregate into folder matches.
                    var matches = await analyzer.AggregateFolderMatchesAsync(files, cache, progress, token);
                    token.ThrowIfCancellationRequested();
                    return (cache, matches);
                }, token);
                token.ThrowIfCancellationRequested();
                // Update results on UI thread.
                SetCache(result.cache);
                SelectedFolderMatch = null;
                ClearFileDetails();
                _allFolderMatches = result.matches;
                _hasAnalysis = true;
                // Apply filters and populate results.
                await ApplyFiltersCoreAsync();
                StatusMessage = $"Analysis complete: {FilteredFolderMatches.Count} of {_allFolderMatches.Count} folder pairs shown" + ErrorMessage();
                return true;
            }
            catch (OperationCanceledException) { StatusMessage = "Comparison cancelled; previous results retained"; return false; }
            catch (Exception ex) { StatusMessage = $"Comparison failed; previous results retained. {ex.Message}"; return false; }
            finally { EndOperation(); }
        }

        public async Task ApplyFiltersAsync()
        {
            if (!CanApplyFilters) return;
            try { await ApplyFiltersCoreAsync(); }
            catch (Exception ex) { StatusMessage = $"Error applying filters: {ex.Message}"; }
        }

        private async Task ApplyFiltersCoreAsync()
        {
            var version = ++_filterVersion;
            // Track filter application so controls remain disabled until it completes.
            IsPopulatingResults = true;
            try
            {
                var criteria = CurrentFilters();
                var matches = _allFolderMatches;
                // Run filtering on background thread.
                var filtered = await Task.Run(() => _duplicateAnalyzer.ApplyFilters(matches, criteria));
                if (_disposed || version != _filterVersion) return;
                // Clear existing results on UI thread.
                FilteredFolderMatches.Clear();
                // Add results in batches to keep UI responsive.
                for (var i = 0; i < filtered.Count; i++)
                {
                    if (_disposed || version != _filterVersion) return;
                    FilteredFolderMatches.Add(filtered[i]);
                    // Allow UI to update by yielding to the captured UI context.
                    if (i % 50 == 49) await Task.Yield();
                }
                if (SelectedFolderMatch != null && !filtered.Contains(SelectedFolderMatch))
                    SelectedFolderMatch = null;
                // Update status when result population completes.
                StatusMessage = $"Filter applied: showing {filtered.Count} folder pairs";
            }
            finally
            {
                if (version == _filterVersion) IsPopulatingResults = false;
            }
        }

        public void CancelOperation()
        {
            if (_cancellationTokenSource == null) return;
            _cancellationTokenSource.Cancel();
            StatusMessage = "Cancelling operation...";
        }

        public async Task<bool> SaveProjectAsync(string filePath)
        {
            if (!CanSaveProject) return false;
            try
            {
                BeginOperation("Saving project...", cancellable: false);
                var data = _cacheManager.ExportToProjectData(_scanFolders);
                data.CreatedDate = _createdDate;
                data.DuplicateFiles = _allFolderMatches.SelectMany(m => m.DuplicateFiles).ToList();
                data.HasAnalysis = _hasAnalysis;
                data.Filters = CurrentFilters();
                data.ShowUniqueFiles = ShowUniqueFiles;
                data.SelectedLeftFolder = SelectedFolderMatch?.LeftFolder;
                data.SelectedRightFolder = SelectedFolderMatch?.RightFolder;
                await Task.Run(() => _projectManager.SaveProjectAsync(filePath, data));
                StatusMessage = "Project saved successfully";
                return true;
            }
            catch (Exception ex) { StatusMessage = $"Error saving project: {ex.Message}"; return false; }
            finally { EndOperation(); }
        }

        public async Task<bool> LoadProjectAsync(string filePath)
        {
            if (!CanLoadProject) return false;
            try
            {
                var token = BeginOperation("Loading project...");
                var restored = await Task.Run(async () =>
                {
                    var data = await _projectManager.LoadProjectAsync(filePath, token);
                    token.ThrowIfCancellationRequested();
                    var cache = new CacheManager();
                    cache.LoadFromProjectData(data);
                    var matches = await new DuplicateAnalyzer(cache, _errorRecoveryService)
                        .AggregateFolderMatchesAsync(data.DuplicateFiles, cache, cancellationToken: token);
                    return (data, cache, matches);
                }, token);
                token.ThrowIfCancellationRequested();
                // Only replace the active project after parsing and validation succeed.
                SetCache(restored.cache);
                // Clear existing results.
                ClearAnalysis();
                _scanFolders.Clear();
                ScanFolders.Clear();
                // Retain saved roots even when their drives are unavailable;
                // folder existence is validated when a fresh comparison is requested.
                foreach (var root in restored.data.ScanFolders)
                {
                    _scanFolders.Add(root);
                    ScanFolders.Add(root);
                }
                _createdDate = restored.data.CreatedDate;
                _hasAnalysis = restored.data.HasAnalysis;
                _allFolderMatches = restored.matches;
                MinimumSimilarity = restored.data.Filters.MinimumSimilarityPercent;
                MinimumSizeMB = restored.data.Filters.MinimumSizeBytes / (1024.0 * 1024);
                ShowUniqueFiles = restored.data.ShowUniqueFiles;
                await ApplyFiltersCoreAsync();
                await SelectFolderMatchAsync(FilteredFolderMatches.FirstOrDefault(m =>
                    string.Equals(m.LeftFolder, restored.data.SelectedLeftFolder, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.RightFolder, restored.data.SelectedRightFolder, StringComparison.OrdinalIgnoreCase)));
                StatusMessage = $"Project restored: {ScanFolders.Count} roots, {FilteredFolderMatches.Count} folder pairs. Showing the saved snapshot; run comparison to refresh.";
                return true;
            }
            catch (OperationCanceledException) { StatusMessage = "Project load cancelled; previous project retained"; return false; }
            catch (Exception ex) { StatusMessage = $"Error loading project; previous project retained. {ex.Message}"; return false; }
            finally { EndOperation(); }
        }

        public Task SelectFolderMatchAsync(FolderMatch? match)
        {
            SetProperty(ref _selectedFolderMatch, match, nameof(SelectedFolderMatch));
            return UpdateFileDetailsAsync();
        }

        private FilterCriteria CurrentFilters()
        {
            if (!double.IsFinite(MinimumSimilarity) || MinimumSimilarity is < 0 or > 100 ||
                !double.IsFinite(MinimumSizeMB) || MinimumSizeMB < 0 || MinimumSizeMB >= long.MaxValue / (1024.0 * 1024))
                throw new ArgumentOutOfRangeException(nameof(MinimumSimilarity), "Enter a similarity from 0 to 100 and a nonnegative folder size.");
            return new FilterCriteria
            {
                MinimumSimilarityPercent = MinimumSimilarity,
                MinimumSizeBytes = (long)(MinimumSizeMB * 1024 * 1024)
            };
        }

        private void SetCache(ICacheManager cache)
        {
            _cacheManager = cache;
            _folderScanner = new FolderScanner(cache, _errorRecoveryService);
            _duplicateAnalyzer = new DuplicateAnalyzer(cache, _errorRecoveryService);
        }

        private CancellationToken BeginOperation(string message, bool cancellable = true)
        {
            _operationVersion++;
            _cancellationTokenSource = cancellable ? new CancellationTokenSource() : null;
            _errorRecoveryService.ClearErrorSummary();
            OperationInProgress = true;
            StatusMessage = message;
            IsProgressIndeterminate = true;
            return _cancellationTokenSource?.Token ?? CancellationToken.None;
        }

        private void EndOperation()
        {
            _operationVersion++;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            OperationInProgress = false;
            ResetProgress();
        }

        private IProgress<AnalysisProgress> CreateProgress()
        {
            var version = _operationVersion;
            return new Progress<AnalysisProgress>(p =>
            {
                if (!_disposed && OperationInProgress && version == _operationVersion) UpdateProgress(p);
            });
        }

        private string ErrorMessage()
        {
            var errors = _errorRecoveryService.GetErrorSummary();
            return errors.HasErrors ? $". Skipped {errors.SkippedFiles} inaccessible items; results may be incomplete." : "";
        }

        private void ClearAnalysis()
        {
            ++_detailVersion;
            _hasAnalysis = false;
            _allFolderMatches.Clear();
            FilteredFolderMatches.Clear();
            SelectedFolderMatch = null;
            ClearFileDetails();
        }

        private void NotifyAvailability()
        {
            OnPropertyChanged(nameof(CanAddFolders));
            OnPropertyChanged(nameof(CanRemoveFolders));
            OnPropertyChanged(nameof(CanRunComparison));
            OnPropertyChanged(nameof(CanSaveProject));
            OnPropertyChanged(nameof(CanLoadProject));
            OnPropertyChanged(nameof(CanApplyFilters));
            OnPropertyChanged(nameof(CanCancel));
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
            var version = ++_detailVersion;
            var match = SelectedFolderMatch;
            ClearFileDetails();
            if (match == null || _disposed) return;
            try
            {
                // Use saved metadata so folder details remain available offline.
                var cache = _cacheManager;
                // Build file comparison on background thread.
                var details = await Task.Run(() => _fileComparer.BuildFileComparison(
                    match.LeftFolder, match.RightFolder, match.DuplicateFiles, cache));
                if (_disposed || version != _detailVersion) return;
                // Update folder displays.
                LeftFolderDisplay = match.LeftFolder;
                RightFolderDisplay = match.RightFolder;
                _allFileDetails = details;
                FilterFileDetails();
            }
            catch (Exception ex)
            {
                // Report the error but don't crash the UI.
                if (version == _detailVersion && !_disposed)
                    StatusMessage = $"Error updating file details: {ex.Message}";
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
                    
                    // Dispose the cancellation token source when the running operation ends.
                    // The running operation owns and disposes its token source.
                    ++_detailVersion;
                    ++_filterVersion;
                }
                _disposed = true;
            }
        }
        #endregion
    }
}
