// FolderDupFinder  –  MainWindow.cs
// -----------------------------------------------------------------------------
//  • Users add unlimited folders to a scan list.
//  • Each folder scan is cached; SHA-256 hashes are cached.
//  • Parallel file-first duplicate detection across ALL folders.
//  • Folder similarity (%) computed, similar folders nested.
//  • Results: sortable ListView (Similarity) + nested TreeView.
//  • Save / Load project (*.dfp) persists folder list + caches (not working yet)
//  • All WPF types fully qualified (System.Windows.Controls.*, System.Windows.MessageBox).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Timers;
using System.Windows.Threading;
using System.Windows.Media;

namespace FolderDupFinder
{
   public partial class MainWindow : System.Windows.Window
   {
      // ───────── runtime caches / state ─────────
      // List of folders selected by the user for scanning
      private readonly List<string> _scanFolders = new();
      // Cache: maps folder path to its file list
    private readonly ConcurrentDictionary<string, List<string>> _folderFileCache = new(StringComparer.OrdinalIgnoreCase);
      // Cache: maps file path to its SHA256 hash
    private readonly ConcurrentDictionary<string, string> _fileHashCache = new(StringComparer.OrdinalIgnoreCase);

      // Structures for tracking potential and confirmed duplicate files between folder pairs
      private readonly Dictionary<(string, string), int> _potentialSharedFiles = new();
      private readonly Dictionary<(string, string), int> _confirmedDuplicates = new();

      // Cache for folder info (files, size, count) for quick access
    private readonly ConcurrentDictionary<string, FolderInfo> _folderInfoCache = new(StringComparer.OrdinalIgnoreCase);

      // Results: flat representation of folder matches
      private List<FolderMatch> _flatMatches = new();

      // UI update timer for progress bar and status
      private System.Timers.Timer _uiUpdateTimer;
      private int _foldersScanned = 0;
      private int _totalFoldersToScan = 0;
      private string _scanStatus = "";

      // Progress tracking for file comparison phase
      private int _currentProgress = 0;
      private int _maxProgress = 1;
      private bool _isFileComparisonMode = false;
    // True while results ListView is being populated progressively
    private bool _isPopulatingResults = false;

      // Cancellation support
      private CancellationTokenSource? _cancellationTokenSource;
      private bool _operationInProgress = false;

      // Controls parallelism for folder/file processing
      private static readonly int MaxParallelism = Math.Max(1, Environment.ProcessorCount - 1);

      // Stores last file matches for filtering and re-display
      private List<FileMatch> _lastFileMatches = new List<FileMatch>();

      // Observable collection for binding to ListView
      private ObservableCollection<FolderMatch> _folderMatchCollection = new();

      // Enhanced ViewModels and UI state for the WinMerge-style file details panel
      public class FileDetailViewModel
      {
          // Left folder file properties
          public string LeftFileName { get; set; } = "";
          public string LeftSizeDisplay { get; set; } = "";
          public long LeftSizeBytes { get; set; } = 0;
          public string LeftDateDisplay { get; set; } = "";
          public DateTime? LeftDate { get; set; } = null;
          public string LeftFullPath { get; set; } = "";
          
          // Right folder file properties  
          public string RightFileName { get; set; } = "";
          public string RightSizeDisplay { get; set; } = "";
          public long RightSizeBytes { get; set; } = 0;
          public string RightDateDisplay { get; set; } = "";
          public DateTime? RightDate { get; set; } = null;
          public string RightFullPath { get; set; } = "";
          
          // Status properties
          public bool IsDuplicate { get; set; } = false;  // True for files that exist in both folders
          public bool HasLeftFile => !string.IsNullOrEmpty(LeftFileName);
          public bool HasRightFile => !string.IsNullOrEmpty(RightFileName);
          
          // Primary file name for sorting (use whichever side has the file)
          public string PrimaryFileName => HasLeftFile ? LeftFileName : RightFileName;
          
          // For tooltip - show full path(s) and dates
          public string TooltipText
          {
              get
              {
                  if (HasLeftFile && HasRightFile)
                      return $"Left: {LeftFullPath} ({LeftDateDisplay})\nRight: {RightFullPath} ({RightDateDisplay})";
                  else if (HasLeftFile)
                      return $"Left only: {LeftFullPath} ({LeftDateDisplay})";
                  else
                      return $"Right only: {RightFullPath} ({RightDateDisplay})";
              }
          }
      }

      private ObservableCollection<FileDetailViewModel> _fileDetailCollection = new();
      private List<FileDetailViewModel> _allFileDetails = new(); // Includes unique files
      private bool _showUniqueFiles = false;

      // Multi-sorting state for file details ListView
      private readonly List<(string Property, ListSortDirection Direction, int Priority)> _filesSortOrder = new();

      // Track multi-sort state for ListView columns
      private readonly List<(string Property, ListSortDirection Direction, int Priority)> _sortOrder = new();
      private const int MaxSortLevels = 3; // Maximum number of sort levels to show

      // Add this field to track subfolder counts for each scan folder
      private readonly Dictionary<string, int> _scanFolderSubfolderCounts = new();

      public MainWindow()
      {
         InitializeComponent();
         // Set static reference for thread-safe access
         _staticFolderInfoCache = _folderInfoCache;
         // Timer for batching UI updates (progress/status)
         _uiUpdateTimer = new System.Timers.Timer(200); // 200ms batching
         _uiUpdateTimer.Elapsed += (s, e) =>
         {
            Dispatcher.Invoke(() =>
            {
               if (_isFileComparisonMode)
               {
                  mainProgressBar.Maximum = _maxProgress;
                  mainProgressBar.Value = _currentProgress;
               }
               else
               {
                  mainProgressBar.Maximum = _totalFoldersToScan;
                  mainProgressBar.Value = _foldersScanned;
               }
               statusLabel.Text = _scanStatus;
            });
         };
         UpdateButtonStates();
         // Update button states when folder selection changes
         listBoxFolders.SelectionChanged += (s, e) => UpdateButtonStates();
      }

      // Updates the enabled/disabled state of action buttons based on current state
      private void UpdateButtonStates()
      {
         bool hasFolders = _scanFolders.Count > 0;
         bool hasSelection = listBoxFolders.SelectedItem != null;
         bool analysisActive = _operationInProgress;
         
         if (btnAddFolder != null) btnAddFolder.IsEnabled = !analysisActive;
         if (btnRemoveFolder != null) btnRemoveFolder.IsEnabled = hasSelection && !analysisActive;
         if (btnRunComparison != null) btnRunComparison.IsEnabled = hasFolders && !analysisActive;
         if (btnSaveProject != null) btnSaveProject.IsEnabled = hasFolders && !analysisActive;
         if (btnLoadProject != null) btnLoadProject.IsEnabled = !analysisActive;
         if (btnApplyFilters != null) btnApplyFilters.IsEnabled = !_isPopulatingResults && !analysisActive && _flatMatches.Count > 0;
         if (btnClearSort != null) btnClearSort.IsEnabled = !_isPopulatingResults && !analysisActive;
         if (btnCancel != null) 
         {
             btnCancel.IsEnabled = analysisActive;
             btnCancel.Visibility = analysisActive ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
         }
      }

      // ═════════════════════════════ BUTTONS ═════════════════════════════
      // Handler for Add Folder button: opens folder dialog and adds folder to scan list
      private async void AddFolder_Click(object sender, System.Windows.RoutedEventArgs e)
      {
          var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Add folder to scan" };
          if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
          string path = dlg.SelectedPath;
          if (_scanFolders.Contains(path))
          {
              System.Windows.MessageBox.Show("Folder already in list.");
              return;
          }

          // Set up cancellation
          _cancellationTokenSource = new CancellationTokenSource();
          _operationInProgress = true;
          var cancellationToken = _cancellationTokenSource.Token;
          
          btnAddFolder.IsEnabled = false;
          statusLabel.Text = "Counting subfolders...";
          mainProgressBar.IsIndeterminate = true;
          UpdateButtonStates();

          try
          {
              // Count subfolders asynchronously
              await Task.Run(() =>
              {
                  var subfolders = new List<string>();
                  var stack = new Stack<string>();
                  stack.Push(path);

                  while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
                  {
                      var current = stack.Pop();
                      subfolders.Add(current);

                      try
                      {
                          foreach (var dir in Directory.GetDirectories(current))
                          {
                              if (cancellationToken.IsCancellationRequested) break;
                              stack.Push(dir);
                          }

                          if (subfolders.Count % 500 == 0) // Reduced frequency for better performance
                          {
                              Dispatcher.Invoke(() => statusLabel.Text = $"Counting subfolders: {subfolders.Count}...");
                          }
                      }
                      catch (UnauthorizedAccessException) { continue; }
                      catch (DirectoryNotFoundException) { continue; }
                  }

                  if (cancellationToken.IsCancellationRequested)
                  {
                      Dispatcher.Invoke(() => statusLabel.Text = "Operation cancelled.");
                      return;
                  }

                  Dispatcher.Invoke(() =>
                  {
                      _scanFolders.Add(path);
                      listBoxFolders.Items.Add(path);
                      _scanFolderSubfolderCounts[path] = subfolders.Count;
                      _totalFoldersToScan = _scanFolderSubfolderCounts.Values.Sum();
                      mainProgressBar.Maximum = _totalFoldersToScan;
                      mainProgressBar.IsIndeterminate = false;
                      statusLabel.Text = $"Found {subfolders.Count} subfolders. Starting scan...";
                  });

                  // Filter out folders that are already cached
                  var foldersToScan = subfolders.Where(sf => !_folderFileCache.ContainsKey(sf)).ToList();
                  
                  if (foldersToScan.Count == 0)
                  {
                      // All folders already scanned
                      Dispatcher.Invoke(() =>
                      {
                          statusLabel.Text = $"All folders already scanned. Progress: {_foldersScanned}/{_totalFoldersToScan}";
                          mainProgressBar.Value = _foldersScanned;
                          UpdateButtonStates();
                      });
                      return;
                  }

                  // Now scan only the folders that haven't been scanned yet
                  _isFileComparisonMode = false; // Ensure we're in folder scanning mode
                  _uiUpdateTimer.Start();

                  Parallel.ForEach(foldersToScan, new ParallelOptions { 
                      MaxDegreeOfParallelism = MaxParallelism, 
                      CancellationToken = cancellationToken 
                  }, subfolder =>
                  {
                      if (cancellationToken.IsCancellationRequested) return;
                      
                      try
                      {
                          var files = Directory.GetFiles(subfolder, "*.*", SearchOption.TopDirectoryOnly);
                          _folderFileCache[subfolder] = files.ToList();
                          
                          var info = new FolderInfo 
                          { 
                              Files = files.ToList(), 
                              TotalSize = files.Sum(f => new FileInfo(f).Length) 
                          };
                          _folderInfoCache[subfolder] = info;

                          // Increment global folders scanned and update progress bar
                          int scanned = Interlocked.Increment(ref _foldersScanned);
                          if (scanned % 50 == 0 || scanned == _totalFoldersToScan)
                          {
                              _scanStatus = $"Scanning: {scanned}/{_totalFoldersToScan} folders...";
                          }
                      }
                      catch (OperationCanceledException) { return; }
                      catch (Exception) { /* Skip problematic folders */ }
                  });
                  _uiUpdateTimer.Stop();
                  
                  if (cancellationToken.IsCancellationRequested)
                  {
                      Dispatcher.Invoke(() => statusLabel.Text = "Scan cancelled.");
                  }
                  else
                  {
                      Dispatcher.Invoke(() =>
                      {
                          mainProgressBar.Value = _foldersScanned;
                          statusLabel.Text = $"Scan complete: {_foldersScanned}/{_totalFoldersToScan} folders processed";
                      });
                  }
              }, cancellationToken);
          }
          catch (OperationCanceledException)
          {
              statusLabel.Text = "Operation cancelled.";
          }
          catch (Exception ex)
          {
              System.Windows.MessageBox.Show($"Error scanning folder: {ex.Message}");
          }
          finally
          {
              _operationInProgress = false;
              _cancellationTokenSource?.Dispose();
              _cancellationTokenSource = null;
              btnAddFolder.IsEnabled = true;
              mainProgressBar.IsIndeterminate = false;
              UpdateButtonStates();
          }
      }

      // Handler for Remove Folder button: removes selected folder from scan list and cache
      private void RemoveFolder_Click(object sender, System.Windows.RoutedEventArgs e)
      {
          if (listBoxFolders.SelectedItem is not string path) return;
          
          // Count how many subfolders of this path were in the cache
          int removedFolderCount = 0;
          var keysToRemove = _folderFileCache.Keys.Where(k => k.StartsWith(path, StringComparison.OrdinalIgnoreCase)).ToList();
          foreach (var key in keysToRemove)
          {
              _folderFileCache.TryRemove(key, out _);
              _folderInfoCache.TryRemove(key, out _);
              removedFolderCount++;
          }
          
          _scanFolders.Remove(path);
          listBoxFolders.Items.Remove(path);
          _scanFolderSubfolderCounts.Remove(path);
          _totalFoldersToScan = _scanFolderSubfolderCounts.Values.Sum();
          _foldersScanned = Math.Max(0, _foldersScanned - removedFolderCount);
          
          Dispatcher.Invoke(() => {
              mainProgressBar.Maximum = _totalFoldersToScan;
              mainProgressBar.Value = _foldersScanned;
              statusLabel.Text = _totalFoldersToScan > 0 ? 
                  $"Ready to scan. Progress: {_foldersScanned}/{_totalFoldersToScan} folders." :
                  "Ready to scan.";
          });
          Dispatcher.Invoke(UpdateButtonStates);
      }

      // Handler for Run Comparison button: scans all folders, computes matches, updates UI
      private void RunComparison_Click(object sender, System.Windows.RoutedEventArgs e)
      {
          if (_scanFolders.Count < 1)
          {
              System.Windows.MessageBox.Show("Add at least one folder.");
              UpdateButtonStates();
              return;
          }

          // Use all cached subfolders for file comparison - this includes subfolders within each added root folder
          // The tool should find duplicates between ANY folders, whether from different roots or within the same root
          // Build comparison folder list strictly from the cache keys to avoid casing/normalization mismatches
          // Limit to folders under the selected roots to keep scope correct
          var allSubfolders = _folderFileCache.Keys
              .Where(k => _scanFolders.Any(root => k.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
              .Distinct()
              .ToList();

          // Check if we have enough folders to compare
          // Even with one root folder, we can compare its subfolders against each other
          if (allSubfolders.Count < 2)
          {
              System.Windows.MessageBox.Show($"Need at least 2 folders to compare. Found {allSubfolders.Count} scanned folder(s).\n\nTip: If you added one root folder, ensure it contains subfolders. The tool finds duplicates between all folders and subfolders, not just between different root folders.");
              UpdateButtonStates();
              return;
          }

          // Set up cancellation
          _cancellationTokenSource = new CancellationTokenSource();
          _operationInProgress = true;
          var cancellationToken = _cancellationTokenSource.Token;

          // Switch to file comparison mode
          _isFileComparisonMode = true;
          _currentProgress = 0;
          _scanStatus = "Starting file comparison...";
          
          Dispatcher.Invoke(() => {
              mainProgressBar.Value = 0;
              mainProgressBar.Maximum = 1;
              statusLabel.Text = _scanStatus;
          });
          
          // Start the UI update timer for file comparison status updates
          _uiUpdateTimer.Start();
          UpdateButtonStates();

          // Capture current filter thresholds on the UI thread for initial display
          var initialThresholds = GetCurrentFilterThresholds();

          Task.Run(() =>
          {
              try
              {
                  // Compute file matches and aggregate folder matches
                  var prog = new Progress<int>(v => {
                      _currentProgress = v;
                  });
                  var fileMatches = ComputeFileMatches(prog, allSubfolders, cancellationToken);
                  
                  if (cancellationToken.IsCancellationRequested)
                  {
                      SafeUpdateStatus("Comparison cancelled.");
                      return;
                  }
                  
                  _lastFileMatches = fileMatches;
                  _flatMatches = AggregateFolderMatches(fileMatches, cancellationToken);
                  
                  if (cancellationToken.IsCancellationRequested)
                  {
                      SafeUpdateStatus("Comparison cancelled.");
                      return;
                  }
                  
                  // Apply default filters automatically for the initial display (captured from UI thread)
                  var (minSim, minSize) = initialThresholds;
                  var initial = _flatMatches.Where(f => f.Similarity >= minSim && f.SizeBytes >= minSize).ToList();
                  // Populate ListView progressively with progress tracking using the initial filtered set
                  PopulateListViewWithProgress(initial, cancellationToken);
              }
              catch (OperationCanceledException)
              {
                  SafeUpdateStatus("Comparison cancelled.");
              }
              finally
              {
                  _uiUpdateTimer.Stop();
                  _isFileComparisonMode = false;
                  _operationInProgress = false;
                  _cancellationTokenSource?.Dispose();
                  _cancellationTokenSource = null;

                  SafeUpdateButtons();
              }
          }, cancellationToken);
      }

      // Reads current filter thresholds from the UI with safe defaults
      private (double minSimilarity, long minSizeBytes) GetCurrentFilterThresholds()
      {
          // Must be called on UI thread - caller's responsibility
          if (!Dispatcher.CheckAccess())
              throw new InvalidOperationException("GetCurrentFilterThresholds must be called on UI thread");

          string simText = txtMinSimilarity?.Text ?? "";
          string sizeText = txtMinSize?.Text ?? "";

          double minSimilarity;
          if (!double.TryParse(simText, out minSimilarity))
              minSimilarity = 50; // default

          double minSizeMb;
          if (!double.TryParse(sizeText, out minSizeMb))
              minSizeMb = 1; // default

          long minSizeBytes = (long)(minSizeMb * 1024 * 1024);
          return (minSimilarity, minSizeBytes);
      }

      // Populates the ListView with folder matches progressively, showing progress updates
      private void PopulateListViewWithProgress(List<FolderMatch> matches, CancellationToken cancellationToken = default)
      {
          _isPopulatingResults = true;
          Dispatcher.Invoke(UpdateButtonStates);
          _scanStatus = "Populating results view...";
          _maxProgress = matches.Count;
          _currentProgress = 0;
          
          // Clear existing items on UI thread
          Dispatcher.Invoke(() => 
          {
              _folderMatchCollection.Clear();
              listViewFolderMatches.ItemsSource = _folderMatchCollection;
              // Temporarily disable interactions and sorting for stability and speed
              listViewFolderMatches.IsEnabled = false;
              var view = CollectionViewSource.GetDefaultView(listViewFolderMatches.ItemsSource);
              view.SortDescriptions.Clear();
              // Keep user's sort preferences to restore later
              _savedSortOrderDuringPopulate = _sortOrder.ToList();
              _sortOrder.Clear();
          });
          
          int processedMatches = 0;
          const int batchSize = 500; // Larger batches for faster population
          var batch = new List<FolderMatch>();
          try
          {
              foreach (var match in matches)
              {
                  cancellationToken.ThrowIfCancellationRequested();
                  
                  batch.Add(match);
                  processedMatches++;
                  _currentProgress = processedMatches;
                  
                  // Update UI in batches for better performance
                  if (batch.Count >= batchSize || processedMatches == matches.Count)
                  {
                      var currentBatch = batch.ToList(); // Copy for thread safety
                      batch.Clear();
                      
                      // Use BeginInvoke to let UI coalesce work
                      Dispatcher.BeginInvoke(new Action(() =>
                      {
                          foreach (var item in currentBatch)
                              _folderMatchCollection.Add(item);
                      }), DispatcherPriority.Background);
                      
                      _scanStatus = $"Populating results: {processedMatches:N0}/{matches.Count:N0} folder matches added...";
                  }
              }
          }
          catch (OperationCanceledException)
          {
              // Swallow and let caller update status; just ensure state resets in finally
          }
          
          Dispatcher.Invoke(() =>
          {
              // Restore sorting and re-enable interactions now that population is complete
              _sortOrder.Clear();
              foreach (var s in _savedSortOrderDuringPopulate)
                  _sortOrder.Add(s);
              ApplySortingToView();
              UpdateColumnHeaderSortIndicators();
              listViewFolderMatches.IsEnabled = true;
              statusLabel.Text = matches.Count > 0 ? 
                  "Select a group to see details." : 
                  "No duplicate folder groups found. Try lowering filters (Similarity: 0%, Size: 0MB) or check that your folders contain files with the same names and content.";
              mainProgressBar.Value = mainProgressBar.Maximum; // Show 100% completion
          });

          _isPopulatingResults = false;
          Dispatcher.Invoke(UpdateButtonStates);
      }

    // Holds the user's sort during population
    private List<(string Property, ListSortDirection Direction, int Priority)> _savedSortOrderDuringPopulate = new();

      // Handler for Cancel button: cancels the current operation
      private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
      {
          if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
          {
              _cancellationTokenSource.Cancel();
              _scanStatus = "Cancelling operation...";
              Dispatcher.Invoke(() => statusLabel.Text = _scanStatus);
          }
      }

      // Handler for Save Project button: saves current scan state and caches to file
      private void SaveProject_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         var dlg = new Microsoft.Win32.SaveFileDialog
         {
            Filter = "Duplicate Folder Project (*.dfp)|*.dfp",
            DefaultExt = "dfp"
         };
         if (dlg.ShowDialog() != true) return;

         var data = new ProjectData
         {
            Folders = _scanFolders,
            FolderFileCache = _folderFileCache.ToDictionary(k => k.Key, v => v.Value),
            FileHashCache = _fileHashCache.ToDictionary(k => k.Key, v => v.Value)
         };

         File.WriteAllText(dlg.FileName,
             JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
         System.Windows.MessageBox.Show("Project saved.");
         UpdateButtonStates();
      }

      // Handler for Load Project button: loads scan state and caches from file
      private void LoadProject_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         var dlg = new Microsoft.Win32.OpenFileDialog
         {
            Filter = "Duplicate Folder Project (*.dfp)|*.dfp",
            DefaultExt = "dfp"
         };
         if (dlg.ShowDialog() != true) return;

         try
         {
            var data = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(dlg.FileName));
            if (data == null) throw new Exception("Invalid project file.");

            _scanFolders.Clear();
            _scanFolders.AddRange(data.Folders);
            listBoxFolders.ItemsSource = null;
            listBoxFolders.Items.Clear();
            foreach (var f in _scanFolders) listBoxFolders.Items.Add(f);

            _folderFileCache.Clear();
            foreach (var kv in data.FolderFileCache) _folderFileCache[kv.Key] = kv.Value;

            _fileHashCache.Clear();
            foreach (var kv in data.FileHashCache) _fileHashCache[kv.Key] = kv.Value;

            statusLabel.Text = "Project loaded.";
            UpdateButtonStates();
         }
         catch (Exception ex)
         {
            System.Windows.MessageBox.Show($"Load failed: {ex.Message}");
            UpdateButtonStates();
         }
      }

      // ═════════════════════════════ SCAN & CACHE ═════════════════════════════
      // Computes file matches between all folders, using hashes for duplicate detection
      // Returns a list of FileMatch records for all detected duplicate files
      private List<FileMatch> ComputeFileMatches(System.IProgress<int> prog, List<string> allFolders, CancellationToken cancellationToken = default)
      {
          // Phase 1: Build file index with progress tracking
          _scanStatus = $"Building file index from {allFolders.Count} folders (including subfolders)...";
          _maxProgress = allFolders.Count;
          _currentProgress = 0;
          
          int potentialPairs = 0;
          var fileNameSizeToFolders = new Dictionary<(string, long), List<string>>();
          int processedFolders = 0;
          
          foreach (var folder in allFolders)
          {
              cancellationToken.ThrowIfCancellationRequested();

              foreach (var file in _folderFileCache[folder])
              {
                  var info = new FileInfo(file);
                  var key = (info.Name.ToLowerInvariant(), info.Length);
                  if (!fileNameSizeToFolders.TryGetValue(key, out var list))
                     fileNameSizeToFolders[key] = list = new List<string>();
                  if (!list.Contains(folder))
                     list.Add(folder);
              }
              processedFolders++;
              _currentProgress = processedFolders;
              if (processedFolders % 50 == 0)
              {
                  _scanStatus = $"Building file index: {processedFolders}/{allFolders.Count} folders...";
              }
          }
          
          cancellationToken.ThrowIfCancellationRequested();
          
          // Phase 2: Analyze potential duplicates
          _scanStatus = "Analyzing potential duplicates...";
          _potentialSharedFiles.Clear();
          
          // For each file group, count potential shared files between folder pairs
          foreach (var folders in fileNameSizeToFolders.Values.Where(f => f.Count > 1))
          {
              cancellationToken.ThrowIfCancellationRequested();
              
              for (int i = 0; i < folders.Count; i++)
                 for (int j = i + 1; j < folders.Count; j++)
                 {
                    if (folders[i] == folders[j]) continue;
                    var key = (folders[i], folders[j]);
                    var sortedKey = folders[i].CompareTo(folders[j]) < 0 ? key : (folders[j], folders[i]);
                    if (!_potentialSharedFiles.ContainsKey(sortedKey))
                    {
                       _potentialSharedFiles[sortedKey] = 0;
                       potentialPairs++;
                    }
                    _potentialSharedFiles[sortedKey]++;
                 }
          }
          
          cancellationToken.ThrowIfCancellationRequested();
          
          // Phase 3: Group files by name and size with progress tracking
          _scanStatus = $"Found {potentialPairs} potential folder pairs. Grouping files...";
          var allFiles = allFolders.SelectMany(f => _folderFileCache[f]).ToList();
          _maxProgress = allFiles.Count;
          _currentProgress = 0;
          
          var dict = new Dictionary<string, List<string>>();
          int processedFiles = 0;
          
          foreach (var file in allFiles)
          {
              cancellationToken.ThrowIfCancellationRequested();
              
              var key = KeyForFile(file);
              if (!dict.ContainsKey(key)) 
                  dict[key] = new List<string>();
              dict[key].Add(file);
              
              processedFiles++;
              _currentProgress = processedFiles;
              
              if (processedFiles % 1000 == 0 || processedFiles == allFiles.Count)
              {
                  _scanStatus = $"Grouping files: {processedFiles:N0}/{allFiles.Count:N0} files processed...";
              }
          }
          
          // Filter out groups with only one file (no duplicates)
          var duplicateGroups = dict.Where(g => g.Value.Count > 1).ToDictionary(g => g.Key, g => g.Value);
          
          cancellationToken.ThrowIfCancellationRequested();
          
          // Early exit if no potential duplicates found
          if (duplicateGroups.Count == 0)
          {
              _scanStatus = $"No potential duplicate files found. Scanned {dict.Count:N0} unique file types across {allFiles.Count:N0} total files.\n" +
                           $"For duplicates to be found, folders must contain files with identical names AND sizes.\n" +
                           $"Debug: Found {fileNameSizeToFolders.Count:N0} unique file name/size combinations across {allFolders.Count} folders.";
              var emptyResult = new List<FileMatch>();
              return emptyResult;
          }

          // Phase 4: Hash comparison with progress tracking
          _scanStatus = $"Comparing {duplicateGroups.Count:N0} potential duplicate groups...";
          _maxProgress = duplicateGroups.Count;
          _currentProgress = 0;

          var bag = new ConcurrentBag<FileMatch>();
          int processed = 0;
          _confirmedDuplicates.Clear();
          var duplicatePairs = new ConcurrentDictionary<(string, string), int>();

          Parallel.ForEach(duplicateGroups.Values, new ParallelOptions { 
              MaxDegreeOfParallelism = MaxParallelism,
              CancellationToken = cancellationToken 
          }, list =>
          {
             cancellationToken.ThrowIfCancellationRequested();
             
             for (int i = 0; i < list.Count; i++)
             {
                cancellationToken.ThrowIfCancellationRequested();
                
                var folderA = Path.GetDirectoryName(list[i]) ?? "";
                string h1 = GetHash(list[i]);
                if (h1 == string.Empty) continue;

                for (int j = i + 1; j < list.Count; j++)
                {
                   var folderB = Path.GetDirectoryName(list[j]) ?? "";
                   if (folderA == folderB) continue;

                   string h2 = GetHash(list[j]);
                   if (h1 == h2)
                   {
                      bag.Add(new FileMatch(list[i], list[j]));
                      var key = folderA.CompareTo(folderB) < 0 ? (folderA, folderB) : (folderB, folderA);
                      duplicatePairs.AddOrUpdate(key, 1, (_, count) => count + 1);
                   }
                }
             }
             var currentProcessed = Interlocked.Increment(ref processed);
             _currentProgress = currentProcessed;
             if (currentProcessed % 10 == 0 || currentProcessed == duplicateGroups.Count)
             {
                 _scanStatus = $"Processed {currentProcessed:N0} of {duplicateGroups.Count:N0} groups... ({bag.Count:N0} matches found)";
             }
             prog.Report(currentProcessed);
          });

          cancellationToken.ThrowIfCancellationRequested();

          // Store confirmed duplicate counts for each folder pair
          foreach (var pair in duplicatePairs)
          {
             _confirmedDuplicates[pair.Key] = pair.Value;
          }

          var result = bag.ToList();
          _scanStatus = $"Found {result.Count:N0} duplicate files across {_confirmedDuplicates.Count:N0} folder pairs";
          
          // Add diagnostic information for troubleshooting
          if (result.Count == 0)
          {
              _scanStatus += $" (Processed {duplicateGroups.Count:N0} potential groups, checked {allFiles.Count:N0} total files)";
          }
          else if (result.Count > 1000000) // More than 1 million matches
          {
              _scanStatus += $" (WARNING: Very large result set may impact performance)";
          }
          
          return result;
      }

      // Returns a unique key for a file based on its name and size
      private static string KeyForFile(string file)
      {
         var info = new FileInfo(file);
         return info.Name.ToLowerInvariant() + "_" + info.Length;
      }

      // Computes and caches SHA256 hash for a file, returns empty string on error
      private string GetHash(string file)
      {
         if (_fileHashCache.TryGetValue(file, out var h)) return h;
         try
         {
            using var sha = SHA256.Create();
            using var s = File.OpenRead(file);
            h = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "");
            _fileHashCache[file] = h;
            return h;
         }
         catch { return string.Empty; }
      }

      // Aggregates file matches into folder matches (pairwise) with progress tracking
      private List<FolderMatch> AggregateFolderMatches(IEnumerable<FileMatch> files, CancellationToken cancellationToken = default)
      {
          _scanStatus = "Aggregating folder matches...";
          var filesList = files.ToList();
          _maxProgress = filesList.Count;
          _currentProgress = 0;
          
          // Early exit if no file matches
          if (filesList.Count == 0)
          {
              _scanStatus = "No file matches to aggregate into folder matches";
              return new List<FolderMatch>();
          }
          
          // Use ConcurrentDictionary for thread-safe aggregation with large datasets
          var dict = new ConcurrentDictionary<(string, string), ConcurrentBag<FileMatch>>();
          int processedFiles = 0;
          
          // Process in parallel for large datasets
          var parallelOptions = new ParallelOptions 
          { 
              MaxDegreeOfParallelism = MaxParallelism,
              CancellationToken = cancellationToken 
          };
          
          Parallel.ForEach(filesList, parallelOptions, m =>
          {
              cancellationToken.ThrowIfCancellationRequested();
              
              var key = (Path.GetDirectoryName(m.A) ?? "", Path.GetDirectoryName(m.B) ?? "");
              var bag = dict.GetOrAdd(key, _ => new ConcurrentBag<FileMatch>());
              bag.Add(m);
              
              var currentProcessed = Interlocked.Increment(ref processedFiles);
              _currentProgress = currentProcessed;
              
              if (currentProcessed % 10000 == 0 || currentProcessed == filesList.Count)
              {
                  _scanStatus = $"Aggregating matches: {currentProcessed:N0}/{filesList.Count:N0} files processed...";
              }
          });
          
          _scanStatus = "Building folder match objects...";
          var folderPairs = dict.Keys.ToList();
          _maxProgress = folderPairs.Count;
          _currentProgress = 0;

          var result = new List<FolderMatch>();
          int processedPairs = 0;

          foreach (var kvp in dict)
          {
              cancellationToken.ThrowIfCancellationRequested();
              // Compute direct file counts from the cache to avoid cross-thread UI access
              int totalL = 0, totalR = 0;
              if (_folderFileCache.TryGetValue(kvp.Key.Item1, out var lFiles)) totalL = lFiles.Count;
              if (_folderFileCache.TryGetValue(kvp.Key.Item2, out var rFiles)) totalR = rFiles.Count;

              var folderMatch = new FolderMatch(kvp.Key.Item1, kvp.Key.Item2, kvp.Value.ToList(), totalL, totalR);
              result.Add(folderMatch);
              
              processedPairs++;
              _currentProgress = processedPairs;
              
              if (processedPairs % 50 == 0 || processedPairs == folderPairs.Count)
              {
                  _scanStatus = $"Creating folder matches: {processedPairs:N0}/{folderPairs.Count:N0} pairs processed...";
              }
          }

          _scanStatus = $"Built {result.Count:N0} folder matches from {filesList.Count:N0} file matches";
          
          // Add diagnostic information for troubleshooting
          if (result.Count == 0 && filesList.Count > 0)
          {
              _scanStatus += " (File matches found but no folder pairs created - check folder structure)";
          }
          else if (result.Count == 0)
          {
              _scanStatus += " (No duplicate files found to aggregate into folder matches)";
          }
          
          return result;
      }

      // ═════════════════════════════ data records ═════════════════════════════
      // Represents a pair of duplicate files
      public sealed record FileMatch(string A, string B);

      // Represents a pair of folders with their duplicate files and similarity
        public sealed class FolderMatch
        {
            public string Left { get; }
            public string Right { get; }
            public List<FileMatch> Files { get; }
            public double Similarity { get; }
            public string FolderName => Path.GetFileName(Left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            public string SizeDisplay => FormatSize(GetFolderSizeStatic(Left));
            public long SizeBytes => GetFolderSizeStatic(Left);

            public FolderMatch(string l, string r, List<FileMatch> files, int totalL, int totalR)
            {
                Left = l; Right = r; Files = files;
                // Calculate similarity based on direct files only (TopDirectoryOnly), matching detection scope
                Similarity = totalL + totalR > 0 ? (2.0 * files.Count / (totalL + totalR) * 100.0) : 0.0;
            }
        }

      // Data structure for saving/loading project state
      public sealed class ProjectData
      {
         public List<string> Folders { get; set; } = new();
         public Dictionary<string, List<string>> FolderFileCache { get; set; } = new();
         public Dictionary<string, string> FileHashCache { get; set; } = new();
      }

      // Holds all relevant info for a folder (files, size, count)
      public class FolderInfo
      {
         public List<string> Files { get; set; } = new();
         public long TotalSize { get; set; }
         public int FileCount => Files.Count;
      }

      // Helper methods
      private static string FormatSize(long size)
      {
         if (size >= 1L << 30) return $"{size / (1L << 30):N1} GB";
         if (size >= 1L << 20) return $"{size / (1L << 20):N1} MB";
         if (size >= 1L << 10) return $"{size / (1L << 10):N1} KB";
         return $"{size} B";
      }

      private static string FormatDate(DateTime date)
      {
         // Format as "yyyy-MM-dd HH:mm" for consistent display
         return date.ToString("yyyy-MM-dd HH:mm");
      }

      // Gets the latest modification date from all files in a folder
      private DateTime? GetFolderLatestDate(string folderPath)
      {
         if (!_folderFileCache.TryGetValue(folderPath, out var files) || files.Count == 0)
            return null;
            
         DateTime? latestDate = null;
         foreach (var file in files)
         {
            try
            {
               var fileInfo = new FileInfo(file);
               if (!latestDate.HasValue || fileInfo.LastWriteTime > latestDate.Value)
                  latestDate = fileInfo.LastWriteTime;
            }
            catch
            {
               // Skip files that can't be accessed
               continue;
            }
         }
         return latestDate;
      }

      private static long GetFolderSizeStatic(string folder)
      {
         // Avoid cross-thread access to Application.Current.MainWindow
         // Use a static cache reference instead
         return _staticFolderInfoCache?.TryGetValue(folder, out var info) == true ? info.TotalSize : 0;
      }

      // Static reference to cache for thread-safe access
      private static ConcurrentDictionary<string, FolderInfo>? _staticFolderInfoCache;

      // Enhanced multi-column sorting with visual feedback and Ctrl+Click support
      private void GridViewColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         if (_isPopulatingResults) 
         { 
             SafeUpdateStatus("Please wait until results finish populating to change sorting."); 
             return; 
         }
         if (listViewFolderMatches.ItemsSource == null) return;
         
         var header = e.OriginalSource as GridViewColumnHeader;
         if (header?.Content?.ToString() is not string headerText) return;

         string property = headerText switch
         {
            "Similarity (%)" => "Similarity",
            "Similarity %" => "Similarity",  // Updated for new header
            "Sim%" => "Similarity",          // Keep for backward compatibility
            "Size" => "SizeBytes",
            "Folder Name" => "FolderName",
            "Master Folder" => "Left",
            _ => headerText
         };

         // Check if Ctrl key is pressed for multi-column sorting
         bool isMultiSort = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
         
         if (!isMultiSort)
         {
            // Single column sort - clear existing sorts
            _sortOrder.Clear();
         }
         
         // Remove this property if it already exists
         var existingSort = _sortOrder.FirstOrDefault(x => x.Property == property);
         if (existingSort != default)
         {
            _sortOrder.Remove(existingSort);
         }

         // Determine sort direction
         ListSortDirection direction = ListSortDirection.Descending;
         if (existingSort != default)
         {
            // Toggle direction if re-clicking the same column
            direction = existingSort.Direction == ListSortDirection.Ascending 
               ? ListSortDirection.Descending 
               : ListSortDirection.Ascending;
         }

         // Add new sort at the beginning (highest priority)
         _sortOrder.Insert(0, (property, direction, 1));

         // Update priorities and limit sort levels
         for (int i = 0; i < _sortOrder.Count && i < MaxSortLevels; i++)
         {
            var sort = _sortOrder[i];
            _sortOrder[i] = (sort.Property, sort.Direction, i + 1);
         }

         // Remove excess sort levels
         if (_sortOrder.Count > MaxSortLevels)
         {
            _sortOrder.RemoveRange(MaxSortLevels, _sortOrder.Count - MaxSortLevels);
         }

         // Apply sorting to the view
         ApplySortingToView();
         
         // Update column headers with visual indicators
         UpdateColumnHeaderSortIndicators();
      }

      // Enhanced multi-column sorting for file details ListView (WinMerge-style)
      private void FileListGridViewColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         if (listViewFiles.ItemsSource == null) return;
         
         var header = e.OriginalSource as GridViewColumnHeader;
         if (header?.Content?.ToString() is not string headerText) return;

         string property = headerText switch
         {
            "Left File Name" => "LeftFileName",
            "← File Name" => "LeftFileName",    // Updated for new arrow headers
            "Left Size" => "LeftSizeBytes",
            "← Size" => "LeftSizeBytes",        // Updated for new arrow headers
            "Right File Name" => "RightFileName",
            "→ File Name" => "RightFileName",   // Updated for new arrow headers
            "Right Size" => "RightSizeBytes",
            "→ Size" => "RightSizeBytes",       // Updated for new arrow headers
            "File Name" => "PrimaryFileName", // Fallback for compatibility
            _ => headerText
         };

         // Check if Ctrl key is pressed for multi-column sorting
         bool isMultiSort = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
         
         if (!isMultiSort)
         {
            // Single column sort - clear existing sorts
            _filesSortOrder.Clear();
         }
         
         // Remove this property if it already exists
         var existingSort = _filesSortOrder.FirstOrDefault(x => x.Property == property);
         if (existingSort != default)
         {
            _filesSortOrder.Remove(existingSort);
         }

         // Determine sort direction
         ListSortDirection direction = ListSortDirection.Ascending;
         if (existingSort != default)
         {
            // Toggle direction if re-clicking the same column
            direction = existingSort.Direction == ListSortDirection.Ascending 
               ? ListSortDirection.Descending 
               : ListSortDirection.Ascending;
         }

         // Add new sort at the beginning (highest priority)
         _filesSortOrder.Insert(0, (property, direction, 1));

         // Update priorities and limit sort levels
         for (int i = 0; i < _filesSortOrder.Count && i < MaxSortLevels; i++)
         {
            var sort = _filesSortOrder[i];
            _filesSortOrder[i] = (sort.Property, sort.Direction, i + 1);
         }

         // Remove excess sort levels
         if (_filesSortOrder.Count > MaxSortLevels)
         {
            _filesSortOrder.RemoveRange(MaxSortLevels, _filesSortOrder.Count - MaxSortLevels);
         }

         // Apply sorting to the file list view
         ApplyFileListSorting();
         
         // Update column headers with visual indicators
         UpdateFileListColumnHeaders();
      }

      // Applies the current sort order to the ListView
      private void ApplySortingToView()
      {
         var view = CollectionViewSource.GetDefaultView(listViewFolderMatches.ItemsSource);
         view.SortDescriptions.Clear();
         
         foreach (var (property, direction, priority) in _sortOrder)
         {
            view.SortDescriptions.Add(new SortDescription(property, direction));
         }
         
         view.Refresh();
      }

      // Applies the current sort order to the file details ListView
      private void ApplyFileListSorting()
      {
         var view = CollectionViewSource.GetDefaultView(listViewFiles.ItemsSource);
         view.SortDescriptions.Clear();
         
         foreach (var (property, direction, priority) in _filesSortOrder)
         {
            view.SortDescriptions.Add(new SortDescription(property, direction));
         }
         
         view.Refresh();
      }

      // Updates column headers with sort indicators (arrows and priority numbers)
      private void UpdateColumnHeaderSortIndicators()
      {
         // Find the GridView to access headers
         if (listViewFolderMatches.View is not GridView gridView) return;

         foreach (var column in gridView.Columns)
         {
            if (column.Header is string headerText)
            {
               string property = headerText switch
               {
                  "Similarity (%)" => "Similarity",
                  "Similarity %" => "Similarity",  // Updated for new header
                  "Sim%" => "Similarity",         // Updated for backward compatibility
                  "Size" => "SizeBytes", 
                  "Folder Name" => "FolderName",
                  "Master Folder" => "Left",
                  _ => headerText
               };

               var sortInfo = _sortOrder.FirstOrDefault(x => x.Property == property);
               if (sortInfo != default)
               {
                  // Column is being sorted
                  string arrow = sortInfo.Direction == ListSortDirection.Ascending ? " ↑" : " ↓";
                  string priority = _sortOrder.Count > 1 ? $" ({sortInfo.Priority})" : "";
                  
                  // Update header text with indicators
                  string baseHeader = headerText.Split(' ')[0] + 
                     (headerText.Contains("(%)") ? " (%)" : "");
                  column.Header = baseHeader + arrow + priority;
               }
               else
               {
                  // Column is not sorted - restore original header
                  string baseHeader = headerText.Split(' ')[0] + 
                     (headerText.Contains("(%)") ? " (%)" : "");
                  column.Header = baseHeader;
               }
            }
         }
      }

      // Updates file list column headers with sort indicators (WinMerge-style)
      private void UpdateFileListColumnHeaders()
      {
         if (listViewFiles.View is not GridView gridView) return;

         foreach (var column in gridView.Columns)
         {
            if (column.Header is string headerText)
            {
               string property = headerText switch
               {
                  "Left File Name" => "LeftFileName",
                  "← File Name" => "LeftFileName",
                   "Left Size" => "LeftSizeBytes",
                  "← Size" => "LeftSizeBytes",
                  "Right File Name" => "RightFileName", 
                  "→ File Name" => "RightFileName",
                  "Right Size" => "RightSizeBytes",
                  "→ Size" => "RightSizeBytes",
                  "File Name" => "PrimaryFileName", // Fallback
                  _ => headerText
               };

               var sortInfo = _filesSortOrder.FirstOrDefault(x => x.Property == property);
               if (sortInfo != default)
               {
                  // Column is being sorted
                  string arrow = sortInfo.Direction == ListSortDirection.Ascending ? " ↑" : " ↓";
                  string priority = _filesSortOrder.Count > 1 ? $" ({sortInfo.Priority})" : "";
                  
                  // Update header text with indicators while preserving original header text
                  string baseHeader = headerText.Split(' ')[0];
                  if (headerText.Contains("File Name")) 
                     baseHeader = headerText.Contains("Left") ? "Left File Name" : 
                                 headerText.Contains("Right") ? "Right File Name" : "File Name";
                  else if (headerText.Contains("Size"))
                     baseHeader = headerText.Contains("Left") ? "Left Size" : "Right Size";
                  
                  column.Header = baseHeader + arrow + priority;
               }
               else
               {
                  // Column is not sorted - restore original header
                  column.Header = headerText.Split(' ')[0] switch
                  {
                     "Left" when headerText.Contains("File") => "Left File Name",
                     "Left" when headerText.Contains("Size") => "Left Size", 
                     "Right" when headerText.Contains("File") => "Right File Name",
                     "Right" when headerText.Contains("Size") => "Right Size",
                     _ => headerText.Split(' ')[0]
                  };
               }
            }
         }
      }

      // Clears all sorting and resets to default view
      private void ClearAllSorting()
      {
         _sortOrder.Clear();
         ApplySortingToView();
         UpdateColumnHeaderSortIndicators();
      }

      private void ApplyFilters_Click(object sender, RoutedEventArgs e)
      {
         // Prevent filtering while comparison or population is running
         if (_operationInProgress || _isPopulatingResults)
         {
             SafeUpdateStatus("Please wait until the results list is built before applying filters.");
             return;
         }

         double minSimilarity = 0;
         long minSizeBytes = 0;
         double.TryParse(txtMinSimilarity?.Text, out minSimilarity);
         double minSizeMb = 0;
         double.TryParse(txtMinSize?.Text, out minSizeMb);
         minSizeBytes = (long)(minSizeMb * 1024 * 1024);

         var filtered = _flatMatches.Where(f => f.Similarity >= minSimilarity && f.SizeBytes >= minSizeBytes).ToList();

         // Replace the ItemsSource quickly without progressive batching for snappy filtering
         Dispatcher.Invoke(() =>
         {
             var previouslySelected = listViewFolderMatches.SelectedItem as FolderMatch;
             // Capture current scroll offset to restore after rebinding
             var sv = FindScrollViewer(listViewFolderMatches);
             double offset = sv?.VerticalOffset ?? 0;
             _folderMatchCollection = new ObservableCollection<FolderMatch>(filtered);
             listViewFolderMatches.ItemsSource = _folderMatchCollection;
             // Re-apply current sorting to the new view
             ApplySortingToView();
             UpdateColumnHeaderSortIndicators();
             // Restore selection and keep it in view if still present
             if (previouslySelected != null && filtered.Contains(previouslySelected))
             {
                 listViewFolderMatches.SelectedItem = previouslySelected;
                 listViewFolderMatches.ScrollIntoView(previouslySelected);
             }
             // Restore scroll offset after layout pass
             if (sv != null)
             {
                 Dispatcher.BeginInvoke(new Action(() =>
                 {
                     try { sv.ScrollToVerticalOffset(Math.Min(offset, sv.ScrollableHeight)); } catch { }
                 }), DispatcherPriority.Render);
             }
             statusLabel.Text = $"Filter applied: showing {filtered.Count:N0} groups";
         });
      }

      // Utility to find the ScrollViewer inside a control
      private static ScrollViewer? FindScrollViewer(DependencyObject? d)
      {
          if (d == null) return null;
          if (d is ScrollViewer sv) return sv;
          int count = VisualTreeHelper.GetChildrenCount(d);
          for (int i = 0; i < count; i++)
          {
              var child = VisualTreeHelper.GetChild(d, i);
              var result = FindScrollViewer(child);
              if (result != null) return result;
          }
          return null;
      }

      // Enhanced selection changed handler for the new file details interface
      private void listViewFolderMatches_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
      {
          // Clear the file details
          ClearFileDetails();
          
          if (listViewFolderMatches.SelectedItem is not FolderMatch item) return;

          string leftFolder = item.Left;
          string rightFolder = item.Right;
          var duplicateFiles = item.Files;
          
          if (string.IsNullOrEmpty(leftFolder) || string.IsNullOrEmpty(rightFolder) || duplicateFiles == null) return;

          // Get latest modification dates for folders
          var leftDate = GetFolderLatestDate(leftFolder);
          var rightDate = GetFolderLatestDate(rightFolder);
          
          // Update folder path text displays with dates
          txtLeftFolderDisplay.Text = leftDate.HasValue ? 
              $"{leftFolder} ({FormatDate(leftDate.Value)})" : leftFolder;
          txtRightFolderDisplay.Text = rightDate.HasValue ? 
              $"{rightFolder} ({FormatDate(rightDate.Value)})" : rightFolder;
          
          // Build comprehensive file list
          BuildFileDetailsList(leftFolder, rightFolder, duplicateFiles);
      }

      // Event handler for the "Show unique files" checkbox
      private void ShowUniqueFiles_Changed(object sender, System.Windows.RoutedEventArgs e)
      {
          _showUniqueFiles = chkShowUniqueFiles.IsChecked == true;
          FilterFileDetails();
      }

      // Clears the file details panel
      private void ClearFileDetails()
      {
          txtLeftFolderDisplay.Text = "";
          txtRightFolderDisplay.Text = "";
          _allFileDetails.Clear();
          _fileDetailCollection.Clear();
          _filesSortOrder.Clear(); // Clear file sorting as well
          listViewFiles.ItemsSource = null;
          lblFileCount.Text = "0";
      }

      // Builds the comprehensive file details list for the selected folder pair (WinMerge-style)
      private void BuildFileDetailsList(string leftFolder, string rightFolder, List<FileMatch> duplicateFiles)
      {
          _allFileDetails.Clear();
          
          // Get all files from both folders
          var leftFiles = _folderFileCache.TryGetValue(leftFolder, out var leftList) ? leftList : new List<string>();
          var rightFiles = _folderFileCache.TryGetValue(rightFolder, out var rightList) ? rightList : new List<string>();
          
          // Create lookup for duplicate files by file name
          var duplicateFileMap = new Dictionary<string, FileMatch>();
          foreach (var match in duplicateFiles)
          {
              var fileName = Path.GetFileName(match.A).ToLowerInvariant();
              duplicateFileMap[fileName] = match;
          }
          
          // Get all unique file names from both folders
          var allFileNames = new HashSet<string>();
          foreach (var file in leftFiles)
              allFileNames.Add(Path.GetFileName(file).ToLowerInvariant());
          foreach (var file in rightFiles)  
              allFileNames.Add(Path.GetFileName(file).ToLowerInvariant());
          
          // Build file details for each unique file name
          foreach (var fileName in allFileNames.OrderBy(f => f))
          {
              var leftFile = leftFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
              var rightFile = rightFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
              
              var isDuplicate = duplicateFileMap.ContainsKey(fileName);
              
              var fileDetail = new FileDetailViewModel
              {
                  IsDuplicate = isDuplicate
              };
              
              // Populate left folder file info
              if (leftFile != null)
              {
                  var leftInfo = new FileInfo(leftFile);
                  fileDetail.LeftFileName = Path.GetFileName(leftFile);
                  fileDetail.LeftSizeDisplay = FormatSize(leftInfo.Length);
                  fileDetail.LeftSizeBytes = leftInfo.Length;
                  fileDetail.LeftDateDisplay = FormatDate(leftInfo.LastWriteTime);
                  fileDetail.LeftDate = leftInfo.LastWriteTime;
                  fileDetail.LeftFullPath = leftFile;
              }
              
              // Populate right folder file info  
              if (rightFile != null)
              {
                  var rightInfo = new FileInfo(rightFile);
                  fileDetail.RightFileName = Path.GetFileName(rightFile);
                  fileDetail.RightSizeDisplay = FormatSize(rightInfo.Length);
                  fileDetail.RightSizeBytes = rightInfo.Length;
                  fileDetail.RightDateDisplay = FormatDate(rightInfo.LastWriteTime);
                  fileDetail.RightDate = rightInfo.LastWriteTime;
                  fileDetail.RightFullPath = rightFile;
              }
              
              _allFileDetails.Add(fileDetail);
          }
          
          // Apply current filter
          FilterFileDetails();
      }

      // Filters the file details based on the current settings (WinMerge-style)
      private void FilterFileDetails()
      {
          var filteredFiles = _showUniqueFiles ? 
              _allFileDetails : 
              _allFileDetails.Where(f => f.IsDuplicate).ToList();
          
          _fileDetailCollection = new ObservableCollection<FileDetailViewModel>(filteredFiles);
          listViewFiles.ItemsSource = _fileDetailCollection;
          
          // Update file count
          var duplicateCount = _allFileDetails.Count(f => f.IsDuplicate);
          var uniqueCount = _allFileDetails.Count(f => !f.IsDuplicate);
          
          if (_showUniqueFiles)
          {
              lblFileCount.Text = $"{_allFileDetails.Count} total ({duplicateCount} duplicates, {uniqueCount} unique)";
          }
          else
          {
              lblFileCount.Text = $"{duplicateCount} duplicates";
          }
      }

      // Handler for Clear Sort button - resets all sorting to default
      private void ClearSort_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         ClearAllSorting();
         statusLabel.Text = "Sorting cleared - showing default order.";
      }

      // Handler for Clear File Sort button - resets file sorting to default
      private void ClearFileSort_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         _filesSortOrder.Clear();
         ApplyFileListSorting();
         UpdateFileListColumnHeaders();
         statusLabel.Text = "File sorting cleared - showing default order.";
      }

        // Safely update UI elements from background threads, tolerant to dispatcher cancellation/shutdown
        private void SafeUpdateStatus(string text)
        {
            try
            {
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => statusLabel.Text = text));
            }
            catch (TaskCanceledException) { /* Ignore during app shutdown/cancellation */ }
            catch (InvalidOperationException) { /* Dispatcher unavailable */ }
        }

        private void SafeUpdateButtons()
        {
            try
            {
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateButtonStates));
            }
            catch (TaskCanceledException) { }
            catch (InvalidOperationException) { }
        }
   }
}
