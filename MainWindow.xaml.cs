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

      // Controls parallelism for folder/file processing
      private static readonly int MaxParallelism = Math.Max(1, Environment.ProcessorCount - 1);

      // Stores last file matches for filtering and re-display
      private List<FileMatch> _lastFileMatches = new List<FileMatch>();

      // Observable collection for binding to ListView
      private ObservableCollection<FolderMatch> _folderMatchCollection = new();

      // ViewModels and UI state
      public class FileMatchViewModel
      {
         public string FileName { get; set; } = "";
         public string SizeDisplay { get; set; } = "";
         public long SizeBytes { get; set; }
         public string Affinity { get; set; } = ""; // "Left", "Right", "Both"
         public string PathA { get; set; } = ""; // For tooltip only
         public string PathB { get; set; } = ""; // For tooltip only
      }

      private List<FileMatchViewModel> _currentFileViewModels = new();
      private List<FileMatchViewModel> _currentFileViewModelsFiltered = new();
      private string _currentLeft = "";
      private string _currentRight = "";
      private List<FileMatch> _currentFiles = new();
      private bool _fileListSortBySizeDesc = false;

      // Track multi-sort state for ListView columns
      private readonly List<(string Property, ListSortDirection Direction)> _sortOrder = new();

      public MainWindow()
      {
         InitializeComponent();
         // Timer for batching UI updates (progress/status)
         _uiUpdateTimer = new System.Timers.Timer(200); // 200ms batching
         _uiUpdateTimer.Elapsed += (s, e) =>
         {
            Dispatcher.Invoke(() =>
            {
               mainProgressBar.Maximum = _totalFoldersToScan;
               mainProgressBar.Value = _foldersScanned;
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
         bool analysisActive = _uiUpdateTimer.Enabled;
         if (btnAddFolder != null) btnAddFolder.IsEnabled = !analysisActive;
         if (btnRemoveFolder != null) btnRemoveFolder.IsEnabled = hasSelection && !analysisActive;
         if (btnRunComparison != null) btnRunComparison.IsEnabled = hasFolders && !analysisActive;
         if (btnSaveProject != null) btnSaveProject.IsEnabled = hasFolders && !analysisActive;
         if (btnLoadProject != null) btnLoadProject.IsEnabled = !analysisActive;
      }

      // ═════════════════════════════ BUTTONS ═════════════════════════════
      // Handler for Add Folder button: opens folder dialog and adds folder to scan list
      private void AddFolder_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Add folder to scan" };
         if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
         string path = dlg.SelectedPath;
         if (_scanFolders.Contains(path))
         {
            System.Windows.MessageBox.Show("Folder already in list.");
            return;
         }
         _scanFolders.Add(path);
         listBoxFolders.Items.Add(path);
         // Scan folder in background immediately
         Task.Run(() => ScanFolder(path));
         Dispatcher.Invoke(UpdateButtonStates);
      }

      // Handler for Remove Folder button: removes selected folder from scan list and cache
      private void RemoveFolder_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         if (listBoxFolders.SelectedItem is not string path) return;
         _scanFolders.Remove(path);
         listBoxFolders.Items.Remove(path);
         _folderFileCache.TryRemove(path, out _);
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

         // Reset progress and status
         _foldersScanned = 0;
         _scanStatus = "Counting subfolders...";
         Dispatcher.Invoke(() => {
            mainProgressBar.Value = 0;
            mainProgressBar.Maximum = 1;
            statusLabel.Text = _scanStatus;
         });
         _uiUpdateTimer.Start();
         UpdateButtonStates();

         // Run scan and comparison in background
         Task.Run(() =>
         {
            var allSubfolders = new List<string>();
            int counted = 0;
            // Recursively collect all subfolders for all scan roots
            foreach (var root in _scanFolders)
            {
               foreach (var subfolder in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
               {
                  allSubfolders.Add(subfolder);
                  counted++;
                  if (counted % 100 == 0)
                  {
                     Dispatcher.Invoke(() => statusLabel.Text = $"Counting subfolders: {counted}...");
                  }
               }
            }
            Dispatcher.Invoke(() => statusLabel.Text = $"Found {allSubfolders.Count} subfolders. Starting scan...");
            _totalFoldersToScan = allSubfolders.Count;
            _foldersScanned = 0;
            Dispatcher.Invoke(() => {
               mainProgressBar.Maximum = _totalFoldersToScan;
               mainProgressBar.Value = 0;
            });

            // Scan each subfolder in parallel, cache file lists and folder info
            Parallel.ForEach(allSubfolders, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism }, subfolder =>
            {
               if (_folderFileCache.ContainsKey(subfolder)) return;
               var files = Directory.GetFiles(subfolder, "*.*", SearchOption.TopDirectoryOnly);
               _folderFileCache[subfolder] = files.ToList();
               var info = new FolderInfo { Files = files.ToList(), TotalSize = files.Sum(f => new FileInfo(f).Length) };
               _folderInfoCache[subfolder] = info;
               int scanned = Interlocked.Increment(ref _foldersScanned);
               if (scanned % 100 == 0 || scanned == _totalFoldersToScan)
               {
                  Dispatcher.Invoke(() => {
                     mainProgressBar.Value = scanned;
                     statusLabel.Text = $"Scanning: {scanned}/{_totalFoldersToScan} folders...";
                  });
               }
            });

            Dispatcher.Invoke(() => {
               mainProgressBar.Value = _totalFoldersToScan;
               statusLabel.Text = "Finding candidate folders...";
            });

            // Compute file matches and aggregate folder matches
            var prog = new Progress<int>(v =>
                Dispatcher.Invoke(() => mainProgressBar.Value = v));
            var fileMatches = ComputeFileMatches(prog, allSubfolders);
            _lastFileMatches = fileMatches;
            _flatMatches = AggregateFolderMatches(fileMatches);
            Dispatcher.Invoke(() => statusLabel.Text = $"File-first done ({fileMatches.Count} matches). Clustering…");

            _uiUpdateTimer.Stop();
            Dispatcher.Invoke(() =>
            {
               // Update ListView with all matches (unfiltered)
               _folderMatchCollection = new ObservableCollection<FolderMatch>(_flatMatches);
               listViewFolderMatches.ItemsSource = _folderMatchCollection;
               statusLabel.Text = "Select a group to see details.";
               UpdateButtonStates();
            });
         });
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
      private List<FileMatch> ComputeFileMatches(System.IProgress<int> prog, List<string> allFolders)
      {
         // Build a map of (filename, size) to folders containing such files
         int potentialPairs = 0;
         var fileNameSizeToFolders = new Dictionary<(string, long), List<string>>();
         foreach (var folder in allFolders)
         {
            foreach (var file in _folderFileCache[folder])
            {
               var info = new FileInfo(file);
               var key = (info.Name.ToLowerInvariant(), info.Length);
               if (!fileNameSizeToFolders.TryGetValue(key, out var list))
                  fileNameSizeToFolders[key] = list = new List<string>();
               if (!list.Contains(folder))
                  list.Add(folder);
            }
         }
         _potentialSharedFiles.Clear();
         // For each file group, count potential shared files between folder pairs
         foreach (var folders in fileNameSizeToFolders.Values.Where(f => f.Count > 1))
         {
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
         Dispatcher.Invoke(() => _scanStatus = $"fileNameSizeToFolders entries: {fileNameSizeToFolders.Count}");
         Dispatcher.Invoke(() => _scanStatus = $"Found {potentialPairs} potential folder pairs to compare");

         // Group all files by (name+size) and keep only those with duplicates
         var allFiles = allFolders.SelectMany(f => _folderFileCache[f]).ToList();
         Dispatcher.Invoke(() => _scanStatus = $"Total files to group: {allFiles.Count}");
         var dict = allFiles.GroupBy(KeyForFile)
                           .Where(g => g.Count() > 1)
                           .ToDictionary(g => g.Key, g => g.ToList());
         Dispatcher.Invoke(() => _scanStatus = $"Potential duplicate groups: {dict.Count}");

         var bag = new ConcurrentBag<FileMatch>();
         int processed = 0;
         _confirmedDuplicates.Clear();

         // For each group of potential duplicates, compare hashes to confirm duplicates
         Dispatcher.Invoke(() =>
         {
            mainProgressBar.Maximum = dict.Count;
            _scanStatus = $"Comparing {dict.Count} potential duplicate groups...";
         });

         var duplicatePairs = new ConcurrentDictionary<(string, string), int>();

         Parallel.ForEach(dict.Values, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism }, list =>
         {
            for (int i = 0; i < list.Count; i++)
            {
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
            Dispatcher.Invoke(() => _scanStatus = $"Processed {currentProcessed} of {dict.Count} groups...");
            prog.Report(currentProcessed);
         });

         // Store confirmed duplicate counts for each folder pair
         foreach (var pair in duplicatePairs)
         {
            _confirmedDuplicates[pair.Key] = pair.Value;
         }

         var result = bag.ToList();
         Dispatcher.Invoke(() => _scanStatus = $"Found {result.Count} duplicate files across {_confirmedDuplicates.Count} folder pairs");
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

      // Aggregates file matches into folder matches (pairwise)
      private List<FolderMatch> AggregateFolderMatches(IEnumerable<FileMatch> files)
      {
         var dict = new Dictionary<(string, string), List<FileMatch>>();
         foreach (var m in files)
         {
            var key = (Path.GetDirectoryName(m.A), Path.GetDirectoryName(m.B));
            if (!dict.ContainsKey(key)) dict[key] = new List<FileMatch>();
            dict[key].Add(m);
         }
         return dict.Select(kv => new FolderMatch(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
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

      private void GridViewColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
      {
         if (listViewFolderMatches.ItemsSource == null) return;
         var header = (e.OriginalSource as GridViewColumnHeader)?.Content?.ToString();
         string prop = header switch
         {
            "Similarity (%)" => "Similarity",
            "Size" => "SizeBytes",
            "Folder Name" => "FolderName",
            "Master Folder" => "Left",
            _ => "Master"
         };

         _sortOrder.RemoveAll(x => x.Property == prop);

         ListSortDirection dir = ListSortDirection.Descending;
         if (_sortOrder.Count > 0 && _sortOrder[0].Property == prop)
         {
            dir = _sortOrder[0].Direction == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
         }

         _sortOrder.Insert(0, (prop, dir));

         var view = CollectionViewSource.GetDefaultView(listViewFolderMatches.ItemsSource);
         view.SortDescriptions.Clear();
         foreach (var (property, d) in _sortOrder)
         {
            view.SortDescriptions.Add(new SortDescription(property, d));
         }
         view.Refresh();
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
         _folderMatchCollection = new ObservableCollection<FolderMatch>(filtered);
         listViewFolderMatches.ItemsSource = _folderMatchCollection;
      }

      // Selection changed handler (simplified to use TreeView only)
      private void listViewFolderMatches_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
      {
         treeViewMatches.Items.Clear();
         if (listViewFolderMatches.SelectedItem is not FolderMatch item) return;

         string left = item.Left;
         string right = item.Right;
         var files = item.Files;
         if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right) || files == null) return;

         var root = new TreeViewItem { Header = $"{left} ⇄ {right}" };
         foreach (var match in files)
         {
            var fileA = match.A;
            var fileB = match.B;
            var sizeA = new FileInfo(fileA).Length;
            var sizeB = new FileInfo(fileB).Length;
            var fileItem = new TreeViewItem {
                Header = $"{Path.GetFileName(fileA)} ({FormatSize(sizeA)}) ⇄ {Path.GetFileName(fileB)} ({FormatSize(sizeB)})",
                ToolTip = $"{fileA} ⇄ {fileB}"
            };
            root.Items.Add(fileItem);
         }
         root.ToolTip = $"{left} ⇄ {right}";
         treeViewMatches.Items.Add(root);
      }

      // Keep all other existing methods as they are...
   }
}
