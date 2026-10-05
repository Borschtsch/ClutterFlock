using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClutterFlock.Controls;
using ClutterFlock.Core;
using ClutterFlock.Models;
using Panel = System.Windows.Controls.Panel;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace ClutterFlock;

public sealed class ImageComparisonWindow : Window
{
    public ImageComparisonView Preview { get; } = new();
    public FileDetailInfo? CurrentFile { get; private set; }
    public bool IsActionPending { get; private set; }
    public Task PendingAction { get; private set; } = Task.CompletedTask;
    private List<FileDetailInfo> _rows;
    private readonly TextBlock _header = new() { FontSize = 17, FontWeight = FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _position = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _differences = new() { Content = "Differences only", IsChecked = true,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 18, 0),
        ToolTip = "Skip recorded identical images. Includes one-sided and unverified images in this folder pair." };
    private readonly Button _previous, _next;
    private readonly Dictionary<string, Button> _actions = new();
    private readonly Func<string, FileDetailInfo, Task>? _performAction;
    private readonly Func<bool> _canManage;

    public ImageComparisonWindow(FileDetailInfo row, IEnumerable<FileDetailInfo>? rows = null,
        Func<string, FileDetailInfo, Task>? performAction = null, Func<bool>? canManage = null, Style? dangerStyle = null)
    {
        NameScope.SetNameScope(this, new NameScope());
        _rows = Images(rows ?? new[] { row });
        if (!_rows.Contains(row)) _rows.Insert(0, row);
        CurrentFile = row; _performAction = performAction; _canManage = canManage ?? (() => false);
        Width = 1200; Height = 800; MinWidth = 780; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Background = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        var layout = new DockPanel { Margin = new Thickness(16), Background = Brushes.White };
        DockPanel.SetDock(_header, Dock.Top); layout.Children.Add(_header);
        var navigation = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        _previous = AddButton(navigation, "PreviousImage", "← Previous", (_, _) => Navigate(-1));
        _next = AddButton(navigation, "NextImage", "Next →", (_, _) => Navigate(1));
        RegisterName("DifferencesOnly", _differences);
        _differences.Click += (_, _) => RefreshControls();
        navigation.Children.Add(_differences); navigation.Children.Add(_position);
        DockPanel.SetDock(navigation, Dock.Top); layout.Children.Add(navigation);
        var footer = new StackPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        foreach (var (name, label) in new[] { ("OpenA", "Open file A"), ("OpenB", "Open file B"),
            ("DeleteA", "Delete file A…"), ("DeleteB", "Delete file B…"),
            ("MergeToA", "Merge file to A…"), ("MergeToB", "Merge file to B…") })
        {
            var button = AddButton(actions, name, label, (_, _) => PendingAction = RunActionAsync(name));
            if (!name.StartsWith("Open")) button.Style = dangerStyle;
            if (name == "DeleteA") button.Margin = new Thickness(30, 0, 8, 0);
            _actions.Add(name, button);
        }
        footer.Children.Add(new TextBlock { Text = "Actions affect ONLY the displayed file pair. Other files and folders are kept.",
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        footer.Children.Add(actions); footer.Children.Add(_status);
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var close = new Button { Content = "Close", Padding = new Thickness(18, 6, 18, 6) };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); bottom.Children.Add(close);
        bottom.Children.Add(new TextBlock { Text = "Current folder pair · ← / → to navigate · Esc to close",
            Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center });
        footer.Children.Add(bottom);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(Preview); Content = layout;
        Loaded += (_, _) => DisplayCurrent();
        Closing += (_, e) => { if (IsActionPending) e.Cancel = true; };
        Closed += (_, _) => Preview.Clear();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.None)
            { Navigate(e.Key == Key.Left ? -1 : 1); e.Handled = true; }
        };
    }

    private Button AddButton(Panel panel, string name, string label, RoutedEventHandler handler)
    {
        var button = new Button { Name = name, Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        RegisterName(name, button); button.Click += handler; panel.Children.Add(button); return button;
    }

    private static List<FileDetailInfo> Images(IEnumerable<FileDetailInfo> rows) => rows
        .Where(r => ImagePreviewLoader.IsImage(r.LeftFullPath) || ImagePreviewLoader.IsImage(r.RightFullPath)).ToList();
    private bool Included(FileDetailInfo row) => _differences.IsChecked != true || !row.IsDuplicate;
    private int NextIndex(int direction)
    {
        var current = CurrentFile == null ? (direction > 0 ? -1 : _rows.Count) : _rows.IndexOf(CurrentFile);
        for (var i = current + direction; i >= 0 && i < _rows.Count; i += direction)
            if (Included(_rows[i])) return i;
        return -1;
    }
    private void Navigate(int direction)
    {
        if (IsActionPending) return;
        var index = NextIndex(direction);
        if (index < 0) return;
        CurrentFile = _rows[index]; _status.Text = ""; DisplayCurrent();
    }

    public void RefreshFiles(IEnumerable<FileDetailInfo> rows)
    {
        var previous = CurrentFile;
        var index = previous == null ? 0 : _rows.IndexOf(previous);
        _rows = Images(rows);
        CurrentFile = _rows.FirstOrDefault(r => r.LeftFullPath.Equals(previous?.LeftFullPath, StringComparison.OrdinalIgnoreCase)
            && r.RightFullPath.Equals(previous?.RightFullPath, StringComparison.OrdinalIgnoreCase));
        // Keep the surviving side visible after deletion so the user can inspect the result.
        CurrentFile ??= _rows.FirstOrDefault(r => previous != null &&
            (!string.IsNullOrEmpty(r.LeftFullPath) && r.LeftFullPath.Equals(previous.LeftFullPath, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(r.RightFullPath) && r.RightFullPath.Equals(previous.RightFullPath, StringComparison.OrdinalIgnoreCase)));
        CurrentFile ??= _rows.Skip(Math.Max(0, index)).FirstOrDefault(Included) ?? _rows.LastOrDefault(Included);
        DisplayCurrent();
    }

    public void SetStatus(string message) => _status.Text = message;
    public void RefreshControls()
    {
        _previous.IsEnabled = !IsActionPending && NextIndex(-1) >= 0;
        _next.IsEnabled = !IsActionPending && NextIndex(1) >= 0;
        _differences.IsEnabled = !IsActionPending;
        var candidates = _rows.Where(Included).ToList();
        var index = CurrentFile == null ? -1 : candidates.IndexOf(CurrentFile);
        _position.Text = index >= 0 ? $"{index + 1:N0} / {candidates.Count:N0} images"
            : CurrentFile == null ? "0 image candidates" : $"{candidates.Count:N0} candidates · current image outside filter";
        foreach (var (name, button) in _actions)
        {
            var sourceIsA = name.StartsWith("Merge") ? name.EndsWith('B') : name.EndsWith('A');
            var path = sourceIsA ? CurrentFile?.LeftFullPath : CurrentFile?.RightFullPath;
            button.IsEnabled = !IsActionPending && _performAction != null && !string.IsNullOrEmpty(path)
                && (name.StartsWith("Open") || _canManage());
        }
    }
    private void DisplayCurrent()
    {
        Title = "Image comparison" + (CurrentFile == null ? "" : " · " + CurrentFile.PrimaryFileName);
        _header.Text = CurrentFile == null ? "No image candidates remain in this folder pair."
            : CurrentFile.PrimaryFileName + " · " + CurrentFile.Status;
        if (CurrentFile == null) Preview.Clear();
        else Preview.ShowFiles(CurrentFile.LeftFullPath, CurrentFile.RightFullPath, 2560, 0);
        RefreshControls();
    }
    private async Task RunActionAsync(string action)
    {
        if (IsActionPending || CurrentFile is not { } row || _performAction == null || !_actions[action].IsEnabled) return;
        IsActionPending = true; RefreshControls();
        try { await _performAction(action, row); }
        catch (Exception ex) { SetStatus($"Action failed: {ex.Message}"); }
        finally { IsActionPending = false; RefreshControls(); }
    }
}
