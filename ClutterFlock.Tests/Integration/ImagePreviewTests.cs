using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClutterFlock.Controls;
using ClutterFlock.Core;
using ClutterFlock.Models;
using ClutterFlock.ViewModels;

namespace ClutterFlock.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class ImagePreviewTests
{
    private string _root = null!;
    [TestInitialize]
    public void Setup() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ClutterFlockPreviews", Guid.NewGuid().ToString("N"))).FullName;
    [TestCleanup]
    public void Cleanup()
    {
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClutterFlockPreviews")) + Path.DirectorySeparatorChar, Path.GetFullPath(_root));
        Directory.Delete(_root, true);
    }

    private string WriteImage(string name, int width = 1200, int height = 800, ushort orientation = 1)
    {
        var path = Path.Combine(_root, name);
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                pixels[i] = (byte)(x * 255 / width); pixels[i + 1] = (byte)(y * 255 / height); pixels[i + 2] = 100;
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);
        BitmapMetadata? metadata = null;
        BitmapEncoder encoder = new PngBitmapEncoder();
        if (Path.GetExtension(name) == ".jpg")
        {
            encoder = new JpegBitmapEncoder { QualityLevel = 95 };
            metadata = new BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);
        }
        else if (Path.GetExtension(name) == ".tif")
        {
            encoder = new TiffBitmapEncoder();
            metadata = new BitmapMetadata("tiff"); metadata.SetQuery("/ifd/{ushort=274}", orientation);
        }
        else if (Path.GetExtension(name) == ".gif") encoder = new GifBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        if (Path.GetExtension(name) == ".gif") encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
        return path;
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    [DataRow((ushort)1)] [DataRow((ushort)2)] [DataRow((ushort)3)] [DataRow((ushort)4)]
    [DataRow((ushort)5)] [DataRow((ushort)6)] [DataRow((ushort)7)] [DataRow((ushort)8)]
    public async Task RealJpeg_ResizesAndHonorsExif_WithoutChangingOrLockingFile(ushort orientation)
    {
        var path = WriteImage("photo.jpg", 600, 300, orientation);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var preview = await ImagePreviewLoader.LoadAsync(path, 200);
        Assert.IsNotNull(preview.Image, preview.Message);
        Assert.IsTrue(preview.Image.IsFrozen);
        Assert.AreEqual(orientation >= 5 ? 100 : 200, preview.Image.PixelWidth);
        Assert.AreEqual(orientation >= 5 ? 200 : 100, preview.Image.PixelHeight);
        var pixels = new byte[preview.Image.PixelWidth * preview.Image.PixelHeight * 4];
        preview.Image.CopyPixels(pixels, preview.Image.PixelWidth * 4, 0);
        var rgb = new FormatConvertedBitmap(preview.Image, PixelFormats.Rgb24, null, 0);
        var corner = new byte[3]; rgb.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 3, 0);
        var expectedRed = orientation is 2 or 3 or 7 or 8 ? 255 : 0;
        var expectedGreen = orientation is 3 or 4 or 6 or 7 ? 255 : 0;
        Assert.IsLessThan(20, Math.Abs(corner[0] - expectedRed));
        Assert.IsLessThan(20, Math.Abs(corner[1] - expectedGreen));
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task MissingCorruptUnsupportedAndMultiFrameFiles_HaveUsableResults()
    {
        Assert.IsNull((await ImagePreviewLoader.LoadAsync(null, 200)).Image);
        StringAssert.Contains((await ImagePreviewLoader.LoadAsync(Path.Combine(_root, "notes.txt"), 200)).Message, "images only");
        StringAssert.Contains((await ImagePreviewLoader.LoadAsync(Path.Combine(_root, "missing.png"), 200)).Message, "unavailable");
        StringAssert.Contains((await ImagePreviewLoader.LoadAsync(Path.Combine(_root, "missing", "image.png"), 200)).Message, "unavailable");
        var corrupt = Path.Combine(_root, "broken.png"); File.WriteAllText(corrupt, "not an image");
        Assert.IsNull((await ImagePreviewLoader.LoadAsync(corrupt, 200)).Image);
        var path = WriteImage("animation.gif", 80, 40);
        var animated = await ImagePreviewLoader.LoadAsync(path, 200);
        Assert.IsNotNull(animated.Image); StringAssert.Contains(animated.Message, "first frame");
        var tiff = await ImagePreviewLoader.LoadAsync(WriteImage("photo.tif", 120, 60, 6), 200);
        Assert.IsNotNull(tiff.Image); Assert.AreEqual(60, tiff.Image.PixelWidth);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ImagePreviewLoader.LoadAsync(path, 200, cancel.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ImagePreviewLoader.LoadAsync(path, 0));
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task Selection_ReplacesStalePreviews_AndDoubleClickOpensSideBySideWindow()
    {
        var a = WriteImage("A.png"); var b = WriteImage("B.jpg", 600, 900, 6);
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        try
        {
            var model = (MainViewModel)window.DataContext;
            var row = new FileDetailInfo { LeftFileName = "A.png", LeftFullPath = a, RightFileName = "B.jpg", RightFullPath = b, Status = "Different contents" };
            var text = new FileDetailInfo { LeftFileName = "notes.txt", LeftFullPath = Path.Combine(_root, "notes.txt") };
            var list = (ListView)window.FindName("listViewFiles");
            var preview = (ImageComparisonView)window.FindName("imagePreview");
            model.FileDetails.Add(row); model.FileDetails.Add(text);
            list.SelectedItem = row;
            var first = preview.Loading;
            list.SelectedItem = text;
            await first;
            Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)window.FindName("imagePreviewPanel")).Visibility);
            Assert.IsNull(((Image)preview.FindName("leftImage")).Source);
            preview.ShowFiles(a, b, 480, 0);
            var superseded = preview.Loading;
            preview.ShowFiles(b, null, 200, 0);
            await PumpUntilComplete(window, Task.WhenAll(superseded, preview.Loading));
            Assert.AreEqual(200, ((BitmapSource)((Image)preview.FindName("leftImage")).Source).PixelWidth);
            Assert.IsNull(((Image)preview.FindName("rightImage")).Source);
            list.SelectedItem = row; await preview.Loading;
            Assert.IsNotNull(((Image)preview.FindName("leftImage")).Source);
            Assert.IsNotNull(((Image)preview.FindName("rightImage")).Source);
            window.UpdateLayout(); list.ScrollIntoView(row);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.IsGreaterThanOrEqualTo(55.0, ((Image)preview.FindName("leftImage")).ActualHeight,
                "The inline image needs usable space beneath its caption.");
            var item = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);
            Assert.IsNotNull(item);
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            var popup = window.OwnedWindows.OfType<ImageComparisonWindow>().Single();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            await popup.Preview.Loading;
            Assert.IsNotNull(((Image)popup.Preview.FindName("leftImage")).Source);
            Assert.IsNotNull(((Image)popup.Preview.FindName("rightImage")).Source);
            Assert.IsTrue(((BitmapSource)((Image)popup.Preview.FindName("leftImage")).Source).PixelWidth >
                ((BitmapSource)((Image)preview.FindName("leftImage")).Source).PixelWidth);
            Render(window, "image-preview.png"); Render(popup, "image-comparison.png");
            popup.Close();
            var onlyA = new FileDetailInfo { LeftFileName = "A.png", LeftFullPath = a };
            model.FileDetails.Add(onlyA); list.SelectedItem = onlyA; await preview.Loading;
            StringAssert.Contains(((TextBlock)preview.FindName("rightMessage")).Text, "No file");
            // Enter and the visible enlarge button reach the same viewer without opening an external app.
            ((Button)window.FindName("btnEnlargeImage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(1, window.OwnedWindows.OfType<ImageComparisonWindow>().Count());
            window.OwnedWindows.OfType<ImageComparisonWindow>().Single().Close();
            list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            var keyboardPopup = window.OwnedWindows.OfType<ImageComparisonWindow>().Single();
            keyboardPopup.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(keyboardPopup)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.AreEqual(0, window.OwnedWindows.OfType<ImageComparisonWindow>().Count());
            list.SelectedItem = null;
            Assert.IsNull(((Image)preview.FindName("leftImage")).Source);
        }
        finally { window.Hide(); window.Close(); }
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task LargePreview_NavigatesDifferencesAndAllImages_WithoutWrapping()
    {
        var first = new FileDetailInfo { LeftFileName = "first.png", LeftFullPath = WriteImage("first.png", 60, 40), Status = "Only A" };
        var same = new FileDetailInfo { LeftFileName = "same.png", LeftFullPath = WriteImage("same.png", 60, 40), IsDuplicate = true, Status = "Identical" };
        var last = new FileDetailInfo { RightFileName = "last.png", RightFullPath = WriteImage("last.png", 60, 40), Status = "Only B" };
        var text = new FileDetailInfo { LeftFileName = "text.txt", LeftFullPath = Path.Combine(_root, "text.txt") };
        var viewer = new ImageComparisonWindow(first, new[] { first, same, text, last }) { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        viewer.Show();
        try
        {
            Assert.IsTrue(((CheckBox)viewer.FindName("DifferencesOnly")).IsChecked);
            Assert.IsFalse(((Button)viewer.FindName("PreviousImage")).IsEnabled);
            ((Button)viewer.FindName("NextImage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreSame(last, viewer.CurrentFile);
            Assert.IsFalse(((Button)viewer.FindName("NextImage")).IsEnabled);
            await PumpUntilComplete(viewer, viewer.Preview.Loading);
            Assert.IsNotNull(((Image)viewer.Preview.FindName("rightImage")).Source);
            Assert.IsNull(((Image)viewer.Preview.FindName("leftImage")).Source);
            viewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(viewer)!, 0, Key.Left) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.AreSame(first, viewer.CurrentFile);
            var filter = (CheckBox)viewer.FindName("DifferencesOnly");
            filter.IsChecked = false; filter.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            ((Button)viewer.FindName("NextImage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreSame(same, viewer.CurrentFile);
            filter.IsChecked = true; filter.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert.AreSame(same, viewer.CurrentFile, "The deliberately opened image remains visible even when excluded from navigation.");
            ((Button)viewer.FindName("NextImage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreSame(last, viewer.CurrentFile);
            viewer.RefreshFiles(Array.Empty<FileDetailInfo>());
            Assert.IsNull(viewer.CurrentFile);
            Assert.IsNull(((Image)viewer.Preview.FindName("rightImage")).Source);
            Assert.IsFalse(((Button)viewer.FindName("NextImage")).IsEnabled);
            Assert.IsFalse(((Button)viewer.FindName("DeleteA")).IsEnabled);
        }
        finally { viewer.Close(); }
    }

    [STATestMethod(UseSTASynchronizationContext = true)]
    public async Task LargePreview_ConfirmedDeletionChangesOnlyDisplayedSide_AndRefreshesSurvivor()
    {
        var a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        File.Copy(WriteImage("source.png", 60, 40), Path.Combine(a, "same.png"));
        File.Copy(Path.Combine(a, "same.png"), Path.Combine(b, "same.png"));
        File.Copy(WriteImage("different.png", 80, 60), Path.Combine(a, "different.png"));
        File.Copy(WriteImage("other.png", 40, 80), Path.Combine(b, "different.png"));
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        try
        {
            var model = (MainViewModel)window.DataContext;
            Assert.IsTrue(await model.AddFolderAsync(a)); Assert.IsTrue(await model.AddFolderAsync(b));
            var compare = model.RunComparisonAsync(); await PumpUntilComplete(window, compare); Assert.IsTrue(await compare);
            await PumpUntilComplete(window, model.SelectFolderMatchAsync(model.FilteredFolderMatches.Single()));
            var row = model.FileDetails.Single(r => r.Status == "Different contents");
            ((ListView)window.FindName("listViewFiles")).SelectedItem = row;
            ((Button)window.FindName("btnEnlargeImage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var viewer = window.OwnedWindows.OfType<ImageComparisonWindow>().Single();
            Assert.IsTrue(((Button)viewer.FindName("OpenA")).IsEnabled);
            Assert.IsTrue(((Button)viewer.FindName("DeleteA")).IsEnabled);
            var survivor = File.ReadAllBytes(row.RightFullPath);
            ((Button)viewer.FindName("MergeToA")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpUntilComplete(viewer, viewer.PendingAction);
            Assert.IsEmpty(viewer.OwnedWindows.Cast<Window>());
            StringAssert.Contains(model.StatusMessage, "different contents");
            Assert.IsTrue(File.Exists(row.LeftFullPath)); Assert.IsTrue(File.Exists(row.RightFullPath));
            await AnswerDelete(viewer, false, row.LeftFullPath);
            Assert.IsTrue(File.Exists(row.LeftFullPath));
            await AnswerDelete(viewer, true, row.LeftFullPath);
            Assert.IsFalse(File.Exists(row.LeftFullPath));
            CollectionAssert.AreEqual(survivor, File.ReadAllBytes(row.RightFullPath));
            Assert.AreEqual("Only B", viewer.CurrentFile!.Status);
            Assert.IsFalse(((Button)viewer.FindName("DeleteA")).IsEnabled);
            Assert.IsTrue(((Button)viewer.FindName("OpenB")).IsEnabled);
            await PumpUntilComplete(viewer, viewer.Preview.Loading);
            Assert.IsNull(((Image)viewer.Preview.FindName("leftImage")).Source);
            Assert.IsNotNull(((Image)viewer.Preview.FindName("rightImage")).Source);
            Render(viewer, "image-navigation.png");
            await AnswerMerge(viewer, false, row.RightFullPath, row.LeftFullPath);
            Assert.IsTrue(File.Exists(row.RightFullPath)); Assert.IsFalse(File.Exists(row.LeftFullPath));
            await AnswerMerge(viewer, true, row.RightFullPath, row.LeftFullPath);
            Assert.IsFalse(File.Exists(row.RightFullPath));
            CollectionAssert.AreEqual(survivor, File.ReadAllBytes(row.LeftFullPath));
            Assert.AreEqual("Only A", viewer.CurrentFile!.Status);
            Assert.IsTrue(Directory.Exists(a)); Assert.IsTrue(Directory.Exists(b));
            Assert.IsTrue(File.Exists(Path.Combine(a, "same.png"))); Assert.IsTrue(File.Exists(Path.Combine(b, "same.png")));
            model.SelectedFolderMatch = null;
            Assert.IsFalse(viewer.IsVisible, "A viewer must not retain actions after the owner selects a different pair.");
        }
        finally { window.Hide(); window.Close(); }
    }

    private static async Task AnswerMerge(ImageComparisonWindow viewer, bool confirm, string source, string destination)
    {
        Exception? failure = null;
        var observed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var dialog = viewer.OwnedWindows.OfType<ActionConfirmationWindow>().SingleOrDefault();
            if (dialog == null) return;
            timer.Stop(); observed = true;
            try
            {
                var text = ((DockPanel)dialog.Content).Children.OfType<TextBox>().Single().Text;
                StringAssert.Contains(text, "ONLY THE DISPLAYED FILE PAIR");
                StringAssert.Contains(text, source); StringAssert.Contains(text, destination);
                Assert.IsTrue(File.Exists(source)); Assert.IsFalse(File.Exists(destination));
                Assert.IsFalse(((Button)viewer.FindName("DeleteA")).IsEnabled);
            }
            catch (Exception ex) { failure = ex; }
            dialog.DialogResult = failure == null && confirm;
        };
        timer.Start();
        try
        {
            ((Button)viewer.FindName("MergeToA")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpUntilComplete(viewer, viewer.PendingAction);
            Assert.IsTrue(observed);
            if (failure != null) throw failure;
        }
        finally { timer.Stop(); }
    }

    private static async Task AnswerDelete(ImageComparisonWindow viewer, bool confirm, string expectedPath)
    {
        Exception? failure = null;
        var observed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var dialog = viewer.OwnedWindows.OfType<ActionConfirmationWindow>().SingleOrDefault();
            if (dialog == null) return;
            timer.Stop(); observed = true;
            try
            {
                var text = ((DockPanel)dialog.Content).Children.OfType<TextBox>().Single().Text;
                StringAssert.Contains(text, expectedPath);
                StringAssert.Contains(text, "cannot be undone");
                Assert.IsTrue(File.Exists(expectedPath));
                Assert.IsFalse(((Button)viewer.FindName("NextImage")).IsEnabled);
            }
            catch (Exception ex) { failure = ex; }
            dialog.DialogResult = failure == null && confirm;
        };
        timer.Start();
        try
        {
            ((Button)viewer.FindName("DeleteA")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await PumpUntilComplete(viewer, viewer.PendingAction);
            Assert.IsTrue(observed);
            if (failure != null) throw failure;
        }
        finally { timer.Stop(); }
    }

    private static void Render(Window window, string name)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "ClutterFlock.sln"))) repo = repo.Parent;
        var output = Path.Combine(repo!.FullName, "artifacts", "ui-review"); Directory.CreateDirectory(output);
        using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
    }

    private static async Task PumpUntilComplete(Window window, Task work)
    {
        // The MSTest STA context does not run WPF's dispatcher loop while awaiting tasks.
        // Drive queued UI continuations just as Application.Run does in the real application.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!work.IsCompleted && DateTime.UtcNow < deadline)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(10);
        }
        await work.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
