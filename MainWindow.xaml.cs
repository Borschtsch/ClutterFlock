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

namespace FolderDupFinder
{
   public partial class MainWindow : System.Windows.Window
   {
      // ───────── runtime caches / state ─────────
      // List of folders selected by the user for scanning
      private readonly List<string> _scanFolders = new();
      // Cache: maps folder path to its file list
      private readonly ConcurrentDictionary<string, List<string>> _folderFileCache = new();
      // Cache: maps file path to its SHA256 hash
      private readonly ConcurrentDictionary<string, string> _fileHashCache = new();

      // Structures for tracking potential and confirmed duplicate files between folder pairs
      private readonly Dictionary<(string, string), int> _potentialSharedFiles = new();
      private readonly Dictionary<(string, string), int> _confirmedDuplicates = new();
      private readonly Dictionary<(string, long), List<string>> _fileNameSizeToFolders = new();

      // Cache for folder info (files, size, count) for quick access
      private readonly ConcurrentDictionary<string, FolderInfo> _folderInfoCache = new();

      // Results: flat and tree representations of folder matches
      private List<FolderMatch> _flatMatches = new();
      private List<FolderMatchNode> _treeMatches = new();
      private const double SimilarityThreshold = 90.0; // % threshold for nesting in tree view

      // UI update timer for progress bar and status
      private System.Timers.Timer _uiUpdateTimer;
      private int _foldersScanned = 0;
      private int _totalFoldersToScan = 0;
      private string _scanStatus = "";

      // Progress tracking for file comparison phase
      private int _currentProgress = 0;
      private int _maxProgress = 1;
      private bool _isFileComparisonMode = false;

      // Cancellation support
      private CancellationTokenSource _cancellationTokenSource;
      private bool _operationInProgress = false;

      // Controls parallelism for folder/file processing
      private static readonly int MaxParallelism = Math.Max(1, Environment.ProcessorCount - 1);

      // Stores last file matches for filtering and re-display
      private List<FileMatch> _lastFileMatches = new List<FileMatch>();

      // Observable collection for binding to ListView
      private ObservableCollection<FolderMatch> _folderMatchCollection = new();

      // Enhanced ViewModels and UI state for the new file details panel
      public class FileDetailViewModel
      {
          public string FileName { get; set; } = "";
          public string SizeDisplay { get; set; } = "";
          public long SizeBytes { get; set; }
          public string Location { get; set; } = ""; // "Left", "Right", "Both" - renamed from Source/Affinity
          public string FullPathLeft { get; set; } = ""; // Full path in left folder
          public string FullPathRight { get; set; } = ""; // Full path in right folder
          public bool IsUnique { get; set; } // True for unique files, false for duplicates
          
          public string TooltipText => IsUnique ? 
              (string.IsNullOrEmpty(FullPathLeft) ? FullPathRight : FullPathLeft) :
              $"Left: {FullPathLeft}\nRight: {FullPathRight}";
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

          // Set up cancellation
          _cancellationTokenSource = new CancellationTokenSource();
          _operationInProgress = true;
          var cancellationToken = _cancellationTokenSource.Token;

          // Switch to file comparison mode
          _isFileComparisonMode = true;
          _currentProgress = 0;
          _maxProgress = 1;
          _scanStatus = "Starting file comparison...";
          
          Dispatcher.Invoke(() => {
              mainProgressBar.Value = 0;
              mainProgressBar.Maximum = 1;
              statusLabel.Text = _scanStatus;
          });
          
          // Start the UI update timer for file comparison status updates
          _uiUpdateTimer.Start();
          UpdateButtonStates();

          // Use all cached subfolders for file comparison
          var allSubfolders = _scanFolders
              .SelectMany(root =>
                  Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
              .Where(subfolder => _folderFileCache.ContainsKey(subfolder))
              .Distinct()
              .ToList();

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
                      Dispatcher.Invoke(() => statusLabel.Text = "Comparison cancelled.");
                      return;
                  }
                  
                  _lastFileMatches = fileMatches;
                  _flatMatches = AggregateFolderMatches(fileMatches, cancellationToken);
                  
                  if (cancellationToken.IsCancellationRequested)
                  {
                      Dispatcher.Invoke(() => statusLabel.Text = "Comparison cancelled.");
                      return;
                  }
                  
                  // Populate ListView progressively with progress tracking
                  PopulateListViewWithProgress(_flatMatches, cancellationToken);
              }
              catch (OperationCanceledException)
              {
                  Dispatcher.Invoke(() => statusLabel.Text = "Comparison cancelled.");
              }
              finally
              {
                  _uiUpdateTimer.Stop();
                  _isFileComparisonMode = false;
                  _operationInProgress = false;
                  _cancellationTokenSource?.Dispose();
                  _cancellationTokenSource = null;
                  
                  Dispatcher.Invoke(() => UpdateButtonStates());
              }
          }, cancellationToken);
      }

      // Populates the ListView with folder matches progressively, showing progress updates
      private void PopulateListViewWithProgress(List<FolderMatch> matches, CancellationToken cancellationToken = default)
      {
          _scanStatus = "Populating results view...";
          _maxProgress = matches.Count;
          _currentProgress = 0;
          
          // Clear existing items on UI thread
          Dispatcher.Invoke(() => 
          {
              _folderMatchCollection.Clear();
              listViewFolderMatches.ItemsSource = _folderMatchCollection;
          });
          
          int processedMatches = 0;
          const int batchSize = 50; // Add items in batches for better performance
          var batch = new List<FolderMatch>();
          
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
                  
                  Dispatcher.Invoke(() =>
                  {
                      foreach (var item in currentBatch)
                      {
                          _folderMatchCollection.Add(item);
                      }
                  });
                  
                  _scanStatus = $"Populating results: {processedMatches}/{matches.Count} folder matches added...";
                  
                  // Small delay to allow UI to update
                  if (processedMatches < matches.Count)
                  {
                      Thread.Sleep(10);
                  }
              }
          }
          
          Dispatcher.Invoke(() =>
          {
              statusLabel.Text = "Select a group to see details.";
              mainProgressBar.Value = mainProgressBar.Maximum; // Show 100% completion
          });
      }

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
      // Scans a folder and all its subfolders, caching file lists and folder info
      private void ScanFolder(string folder)
      {
         foreach (var subfolder in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Prepend(folder))
         {
            if (_folderFileCache.ContainsKey(subfolder)) continue; // already cached
            var files = Directory.GetFiles(subfolder, "*.*", SearchOption.TopDirectoryOnly);
            _folderFileCache[subfolder] = files.ToList();
            // Cache folder info for UI (size/count)
            var info = new FolderInfo { Files = files.ToList(), TotalSize = files.Sum(f => new FileInfo(f).Length) };
            _folderInfoCache[subfolder] = info;
         }
      }

      // Computes file matches between all folders, using hashes for duplicate detection
      // Returns a list of FileMatch records for all detected duplicate files
      private List<FileMatch> ComputeFileMatches(System.IProgress<int> prog, List<string> allFolders, CancellationToken cancellationToken = default)
      {
          // Phase 1: Build file index with progress tracking
          _scanStatus = "Building file index...";
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
                    var key = folders[i].CompareTo(folders[j]) < 0 ? (folders[i], folders[j]) : (folders[j], folders[i]);
                    if (!_potentialSharedFiles.ContainsKey(key))
                    {
                       _potentialSharedFiles[key] = 0;
                       potentialPairs++;
                    }
                    _potentialSharedFiles[key]++;
                 }
          }
          
          cancellationToken.ThrowIfCancellationRequested();
          
          // Phase 3: Group files by name and size
          _scanStatus = $"Found {potentialPairs} potential folder pairs. Grouping files...";
          var allFiles = allFolders.SelectMany(f => _folderFileCache[f]).ToList();
          
          var dict = allFiles.GroupBy(KeyForFile)
                            .Where(g => g.Count() > 1)
                            .ToDictionary(g => g.Key, g => g.ToList());
          
          cancellationToken.ThrowIfCancellationRequested();
          
          // Phase 4: Hash comparison with progress tracking
          _scanStatus = $"Comparing {dict.Count} potential duplicate groups...";
          _maxProgress = dict.Count;
          _currentProgress = 0;

          var bag = new ConcurrentBag<FileMatch>();
          int processed = 0;
          _confirmedDuplicates.Clear();
          var duplicatePairs = new ConcurrentDictionary<(string, string), int>();

          Parallel.ForEach(dict.Values, new ParallelOptions { 
              MaxDegreeOfParallelism = MaxParallelism,
              CancellationToken = cancellationToken 
          }, list =>
          {
             cancellationToken.ThrowIfCancellationRequested();
             
             for (int i = 0; i < list.Count; i++)
             {
                cancellationToken.ThrowIfCancellationRequested();
                
                var folderA = Path.GetDirectoryName(list[i]);
                string h1 = GetHash(list[i]);
                if (h1 == string.Empty) continue;

                for (int j = i + 1; j < list.Count; j++)
                {
                   var folderB = Path.GetDirectoryName(list[j]);
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
             if (currentProcessed % 10 == 0 || currentProcessed == dict.Count)
             {
                 _scanStatus = $"Processed {currentProcessed} of {dict.Count} groups... ({bag.Count} matches found)";
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
          _scanStatus = $"Found {result.Count} duplicate files across {_confirmedDuplicates.Count} folder pairs";
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
          
          var dict = new Dictionary<(string, string), List<FileMatch>>();
          int processedFiles = 0;
          
          foreach (var m in filesList)
          {
              cancellationToken.ThrowIfCancellationRequested();
              
              var key = (Path.GetDirectoryName(m.A), Path.GetDirectoryName(m.B));
              if (!dict.ContainsKey(key)) dict[key] = new List<FileMatch>();
              dict[key].Add(m);
              
              processedFiles++;
              _currentProgress = processedFiles;
              
              if (processedFiles % 100 == 0 || processedFiles == filesList.Count)
              {
                  _scanStatus = $"Aggregating matches: {processedFiles}/{filesList.Count} files processed...";
              }
          }
          
          _scanStatus = "Building folder match objects...";
          var folderPairs = dict.Keys.ToList();
          _maxProgress = folderPairs.Count;
          _currentProgress = 0;

          var result = new List<FolderMatch>();
          int processedPairs = 0;

          foreach (var kvp in dict)
          {
              cancellationToken.ThrowIfCancellationRequested();
              
              var folderMatch = new FolderMatch(kvp.Key.Item1, kvp.Key.Item2, kvp.Value);
              result.Add(folderMatch);
              
              processedPairs++;
              _currentProgress = processedPairs;
              
              if (processedPairs % 50 == 0 || processedPairs == folderPairs.Count)
              {
                  _scanStatus = $"Creating folder matches: {processedPairs}/{folderPairs.Count} pairs processed...";
              }
          }

          _scanStatus = $"Built {result.Count} folder matches from {filesList.Count} file matches";
          return result;
      }

      // Builds a tree of folder matches for hierarchical display (not used in main ListView)
      private List<FolderMatchNode> BuildFolderMatchTree(IEnumerable<FolderMatch> flat, double threshold)
      {
         var nodes = flat.Select(f => new FolderMatchNode(f)).ToList();
         var roots = new List<FolderMatchNode>();

         foreach (var node in nodes)
         {
            FolderMatchNode parent = null;
            foreach (var candidate in nodes)
            {
               if (candidate == node) continue;
               if (node.Left.StartsWith(candidate.Left, StringComparison.OrdinalIgnoreCase) &&
                   node.Right.StartsWith(candidate.Right, StringComparison.OrdinalIgnoreCase) &&
                   candidate.Similarity >= threshold)
               {
                  if (parent == null || candidate.Left.Length > parent.Left.Length)
                     parent = candidate;
               }
            }
            if (parent != null) parent.Children.Add(node);
            else roots.Add(node);
         }
         return roots;
      }

      // Builds clusters of similar folders using confirmed duplicates (not used in ListView)
      private List<FolderCluster> BuildFolderClusters(List<FileMatch> fileMatches)
      {
         Dispatcher.Invoke(() => _scanStatus = "Building folder clusters...");
         var pairs = _potentialSharedFiles.Keys.ToList();
         var clusters = new List<FolderCluster>();
         var folderToCluster = new Dictionary<string, FolderCluster>(StringComparer.OrdinalIgnoreCase);

         // Build clusters by merging folder pairs with shared files
         foreach (var (a, b) in pairs)
         {
            if (!folderToCluster.TryGetValue(a, out var ca) && !folderToCluster.TryGetValue(b, out var cb))
            {
               var cluster = new FolderCluster { Master = a };
               cluster.Members.Add(a);
               if (a != b) cluster.Members.Add(b);
               folderToCluster[a] = cluster;
               folderToCluster[b] = cluster;
               clusters.Add(cluster);
            }
            else if (folderToCluster.TryGetValue(a, out ca) && !folderToCluster.ContainsKey(b))
            {
               ca.Members.Add(b);
               folderToCluster[b] = ca;
            }
            else if (!folderToCluster.ContainsKey(a) && folderToCluster.TryGetValue(b, out cb))
            {
               cb.Members.Add(a);
               folderToCluster[a] = cb;
            }
            else if (folderToCluster[a] != folderToCluster[b])
            {
               var caMembers = folderToCluster[a].Members;
               var cbMembers = folderToCluster[b].Members;
               caMembers.AddRange(cbMembers);
               foreach (var f in cbMembers) folderToCluster[f] = folderToCluster[a];
               clusters.Remove(folderToCluster[b]);
            }
         }

         Dispatcher.Invoke(() => _scanStatus = $"Computing similarity for {clusters.Count} clusters...");

         // Compute similarity for each cluster (max similarity among all pairs)
         int processedClusters = 0;
         foreach (var cluster in clusters)
         {
            var pairsInCluster = cluster.Members
                .SelectMany((a, i) => cluster.Members.Skip(i + 1)
                .Select(b => (a, b)))
                .Where(p => p.a != p.b);

            double maxSim = 0;
            foreach (var (a, b) in pairsInCluster)
            {
               var key = a.CompareTo(b) < 0 ? (a, b) : (b, a);
               int totalA = _folderFileCache.TryGetValue(a, out var filesA) ? filesA.Count : 0;
               int totalB = _folderFileCache.TryGetValue(b, out var filesB) ? filesB.Count : 0;
               int confirmed = _confirmedDuplicates.TryGetValue(key, out var c) ? c : 0;
               if (totalA + totalB > 0)
               {
                  double sim = 2.0 * confirmed / (totalA + totalB) * 100.0;
                  maxSim = Math.Max(maxSim, sim);
               }
            }
            cluster.Similarity = maxSim;
            processedClusters++;
            if (processedClusters % 10 == 0)
            {
                Dispatcher.Invoke(() => _scanStatus = $"Processed {processedClusters} of {clusters.Count} clusters...");
            }
         }

         // Do NOT remove clusters with zero similarity (restore original behavior)
         // Use the largest folder as master for remaining clusters
         foreach (var cluster in clusters)
         {
            cluster.Master = cluster.Members
                .OrderByDescending(f => _folderFileCache.TryGetValue(f, out var files) ? files.Count : 0)
                .FirstOrDefault();
         }

         Dispatcher.Invoke(() => _scanStatus = $"Found {clusters.Count} folder clusters");
         // Fallback: if no clusters, show all scanned folders as single-member clusters with 0% similarity
         if (clusters.Count == 0)
         {
            Dispatcher.Invoke(() => _scanStatus = "No similar clusters found. Showing all scanned folders.");
            foreach (var folder in _scanFolders)
            {
               clusters.Add(new FolderCluster
               {
                  Master = folder,
                  Members = new List<string> { folder },
                  Similarity = 0.0
               });
            }
         }
         return clusters;
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
         public string SizeDisplay => MainWindow.FormatSize(MainWindow.GetFolderSizeStatic(Left));
         public long SizeBytes => MainWindow.GetFolderSizeStatic(Left);

         public FolderMatch(string l, string r, List<FileMatch> files)
         {
            Left = l; Right = r; Files = files;
            int totalL = Directory.GetFiles(l, "*.*", SearchOption.AllDirectories).Length;
            int totalR = Directory.GetFiles(r, "*.*", SearchOption.AllDirectories).Length;
            Similarity = 2.0 * files.Count / (totalL + totalR) * 100.0;
         }
      }

      // Node for hierarchical folder match tree
      public sealed class FolderMatchNode
      {
         public string Left { get; }
         public string Right { get; }
         public double Similarity { get; }
         public List<FolderMatchNode> Children { get; } = new();

         public FolderMatchNode(FolderMatch f)
         {
            Left = f.Left; Right = f.Right; Similarity = f.Similarity;
         }
      }

      // Represents a cluster of similar folders (not used in ListView)
      public class FolderCluster
      {
         public string Master { get; set; } = ""; // The master folder path
         public List<string> Members { get; set; } = new(); // All similar folders
         public double Similarity { get; set; } // Similarity score for the cluster
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

      private static long GetFolderSizeStatic(string folder)
      {
         var instance = System.Windows.Application.Current.MainWindow as MainWindow;
         if (instance != null && instance._folderInfoCache.TryGetValue(folder, out var info))
            return info.TotalSize;
         return 0;
      }

      // Enhanced multi-column sorting with visual feedback and Ctrl+Click support
      private void GridViewColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         if (listViewFolderMatches.ItemsSource == null) return;
         
         var header = e.OriginalSource as GridViewColumnHeader;
         if (header?.Content?.ToString() is not string headerText) return;

         string property = headerText switch
         {
            "Similarity (%)" => "Similarity",
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

      // Enhanced multi-column sorting for file details ListView
      private void FileListGridViewColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         if (listViewFiles.ItemsSource == null) return;
         
         var header = e.OriginalSource as GridViewColumnHeader;
         if (header?.Content?.ToString() is not string headerText) return;

         string property = headerText switch
         {
            "File Name" => "FileName",
            "Size" => "SizeBytes",
            "Location" => "Location",
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

      // Updates file list column headers with sort indicators
      private void UpdateFileListColumnHeaders()
      {
         if (listViewFiles.View is not GridView gridView) return;

         foreach (var column in gridView.Columns)
         {
            if (column.Header is string headerText)
            {
               string property = headerText switch
               {
                  "File Name" => "FileName",
                  "Size" => "SizeBytes",
                  "Location" => "Location",
                  _ => headerText
               };

               var sortInfo = _filesSortOrder.FirstOrDefault(x => x.Property == property);
               if (sortInfo != default)
               {
                  // Column is being sorted
                  string arrow = sortInfo.Direction == ListSortDirection.Ascending ? " ↑" : " ↓";
                  string priority = _filesSortOrder.Count > 1 ? $" ({sortInfo.Priority})" : "";
                  
                  // Update header text with indicators
                  string baseHeader = headerText.Split(' ')[0];
                  if (headerText.Contains("Name")) baseHeader = "File Name";
                  column.Header = baseHeader + arrow + priority;
               }
               else
               {
                  // Column is not sorted - restore original header
                  column.Header = headerText.Split(' ')[0] == "File" ? "File Name" : headerText.Split(' ')[0];
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
         double minSimilarity = 0;
         long minSizeBytes = 0;
         double.TryParse(txtMinSimilarity?.Text, out minSimilarity);
         double minSizeMb = 0;
         double.TryParse(txtMinSize?.Text, out minSizeMb);
         minSizeBytes = (long)(minSizeMb * 1024 * 1024);

         var filtered = _flatMatches.Where(f => f.Similarity >= minSimilarity && f.SizeBytes >= minSizeBytes).ToList();
         
         // Use progressive population for filtered results too
         Task.Run(() =>
         {
             try
             {
                 _operationInProgress = true;
                 _isFileComparisonMode = true;
                 
                 Dispatcher.Invoke(() => UpdateButtonStates());
                 
                 _uiUpdateTimer.Start();
                 PopulateListViewWithProgress(filtered);
             }
             finally
             {
                 _uiUpdateTimer.Stop();
                 _isFileComparisonMode = false;
                 _operationInProgress = false;
                 
                 Dispatcher.Invoke(() => UpdateButtonStates());
             }
         });
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

          // Update folder path text displays
          txtLeftFolderDisplay.Text = leftFolder;
          txtRightFolderDisplay.Text = rightFolder;
          
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
          listViewFiles.ItemsSource = null;
          lblFileCount.Text = "0";
      }

      // Builds the comprehensive file details list for the selected folder pair
      private void BuildFileDetailsList(string leftFolder, string rightFolder, List<FileMatch> duplicateFiles)
      {
          _allFileDetails.Clear();
          
          // Get all files from both folders
          var leftFiles = _folderFileCache.TryGetValue(leftFolder, out var leftList) ? leftList : new List<string>();
          var rightFiles = _folderFileCache.TryGetValue(rightFolder, out var rightList) ? rightList : new List<string>();
          
          // Create a set of duplicate file names for quick lookup
          var duplicateFileNames = new HashSet<string>();
          var duplicateFileLookup = new Dictionary<string, (string leftPath, string rightPath)>();
          
          foreach (var match in duplicateFiles)
          {
              var leftFileName = Path.GetFileName(match.A);
              var rightFileName = Path.GetFileName(match.B);
              
              duplicateFileNames.Add(leftFileName.ToLowerInvariant());
              duplicateFileLookup[leftFileName.ToLowerInvariant()] = (match.A, match.B);
          }
          
          // Process duplicate files first
          foreach (var match in duplicateFiles)
          {
              var fileName = Path.GetFileName(match.A);
              var fileInfo = new FileInfo(match.A);
              
              _allFileDetails.Add(new FileDetailViewModel
              {
                  FileName = fileName,
                  SizeDisplay = FormatSize(fileInfo.Length),
                  SizeBytes = fileInfo.Length,
                  Location = "Both",
                  FullPathLeft = match.A,
                  FullPathRight = match.B,
                  IsUnique = false
              });
          }
          
          // Process unique files from left folder
          foreach (var filePath in leftFiles)
          {
              var fileName = Path.GetFileName(filePath);
              if (!duplicateFileNames.Contains(fileName.ToLowerInvariant()))
              {
                  var fileInfo = new FileInfo(filePath);
                  _allFileDetails.Add(new FileDetailViewModel
                  {
                      FileName = fileName,
                      SizeDisplay = FormatSize(fileInfo.Length),
                      SizeBytes = fileInfo.Length,
                      Location = "Left",
                      FullPathLeft = filePath,
                      FullPathRight = "",
                      IsUnique = true
                  });
              }
          }
          
          // Process unique files from right folder
          foreach (var filePath in rightFiles)
          {
              var fileName = Path.GetFileName(filePath);
              if (!duplicateFileNames.Contains(fileName.ToLowerInvariant()))
              {
                  var fileInfo = new FileInfo(filePath);
                  _allFileDetails.Add(new FileDetailViewModel
                  {
                      FileName = fileName,
                      SizeDisplay = FormatSize(fileInfo.Length),
                      SizeBytes = fileInfo.Length,
                      Location = "Right",
                      FullPathLeft = "",
                      FullPathRight = filePath,
                      IsUnique = true
                  });
              }
          }
          
          // Sort files by name
          _allFileDetails = _allFileDetails.OrderBy(f => f.FileName).ToList();
          
          // Apply current filter
          FilterFileDetails();
      }

      // Filters the file details based on the current settings
      private void FilterFileDetails()
      {
          var filteredFiles = _showUniqueFiles ? 
              _allFileDetails : 
              _allFileDetails.Where(f => !f.IsUnique).ToList();
          
          _fileDetailCollection = new ObservableCollection<FileDetailViewModel>(filteredFiles);
          listViewFiles.ItemsSource = _fileDetailCollection;
          
          // Update file count
          var duplicateCount = _allFileDetails.Count(f => !f.IsUnique);
          var uniqueCount = _allFileDetails.Count(f => f.IsUnique);
          
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
      // Keep all other existing methods as they are...
   }
}
