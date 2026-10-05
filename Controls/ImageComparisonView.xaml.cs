using System.IO;
using System.Windows.Controls;
using ClutterFlock.Core;
using Image = System.Windows.Controls.Image;
using UserControl = System.Windows.Controls.UserControl;

namespace ClutterFlock.Controls;

public partial class ImageComparisonView : UserControl
{
    private CancellationTokenSource? _loading;
    public Task Loading { get; private set; } = Task.CompletedTask;
    public bool ShowDimensions
    {
        get => leftDimensions.Visibility == System.Windows.Visibility.Visible;
        set => leftDimensions.Visibility = rightDimensions.Visibility = value ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public ImageComparisonView()
    {
        InitializeComponent();
        Unloaded += (_, _) => Clear();
    }

    public void ShowFiles(string? pathA, string? pathB, int maxEdge, int delayMilliseconds = 120)
    {
        Clear();
        var request = _loading = new CancellationTokenSource();
        leftName.Text = "A · " + (string.IsNullOrEmpty(pathA) ? "No file" : Path.GetFileName(pathA));
        rightName.Text = "B · " + (string.IsNullOrEmpty(pathB) ? "No file" : Path.GetFileName(pathB));
        leftName.ToolTip = pathA; rightName.ToolTip = pathB;
        leftMessage.Text = rightMessage.Text = "Loading…";
        Loading = LoadAsync(pathA, pathB, maxEdge, delayMilliseconds, request);
    }

    public void Clear()
    {
        _loading?.Cancel(); _loading = null;
        leftImage.Source = rightImage.Source = null;
        leftName.Text = rightName.Text = leftMessage.Text = rightMessage.Text = leftDimensions.Text = rightDimensions.Text = "";
    }

    private async Task LoadAsync(string? pathA, string? pathB, int maxEdge, int delay, CancellationTokenSource request)
    {
        try
        {
            await Task.Delay(delay, request.Token);
            await Task.WhenAll(LoadSide(pathA, leftImage, leftMessage, leftDimensions),
                LoadSide(pathB, rightImage, rightMessage, rightDimensions));
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_loading, request)) _loading = null;
            request.Dispose();
        }

        async Task LoadSide(string? path, Image image, TextBlock message, TextBlock dimensions)
        {
            var result = await ImagePreviewLoader.LoadAsync(path, maxEdge, request.Token);
            // An old selection must never replace the currently selected file's preview.
            if (request.IsCancellationRequested || !ReferenceEquals(_loading, request)) return;
            image.Source = result.Image;
            image.ToolTip = path + "\n" + result.Message;
            message.Text = result.Image == null ? result.Message : "";
            dimensions.Text = result.Image == null ? "" : result.Message;
        }
    }
}
