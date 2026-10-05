using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClutterFlock.Core;

public sealed record ImagePreview(BitmapSource? Image, string Message);

public static class ImagePreviewLoader
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr", ".webp", ".heic", ".heif", ".avif" };
    private static readonly SemaphoreSlim Decoders = new(2);

    public static bool IsImage(string? path) => !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    public static async Task<ImagePreview> LoadAsync(string? path, int maxEdge, CancellationToken token = default)
    {
        if (maxEdge is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maxEdge));
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(path)) return new(null, "No file on this side");
        if (!IsImage(path)) return new(null, "Preview is available for images only");
        await Decoders.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => Decode(path, maxEdge, token), token).ConfigureAwait(false); }
        finally { Decoders.Release(); }
    }

    private static ImagePreview Decode(string path, int maxEdge, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var width = frame.PixelWidth; var height = frame.PixelHeight;
            var orientation = ReadOrientation(frame);
            token.ThrowIfCancellationRequested();
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            // Decode to the display budget and detach from the stream before returning to WPF.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            if (width >= height) bitmap.DecodePixelWidth = Math.Min(width, maxEdge);
            else bitmap.DecodePixelHeight = Math.Min(height, maxEdge);
            bitmap.EndInit();
            bitmap.Freeze();
            token.ThrowIfCancellationRequested();
            BitmapSource image = bitmap;
            var matrix = orientation switch
            {
                2 => new Matrix(-1, 0, 0, 1, 0, 0),
                3 => new Matrix(-1, 0, 0, -1, 0, 0),
                4 => new Matrix(1, 0, 0, -1, 0, 0),
                5 => new Matrix(0, 1, 1, 0, 0, 0),
                6 => new Matrix(0, 1, -1, 0, 0, 0),
                7 => new Matrix(0, -1, -1, 0, 0, 0),
                8 => new Matrix(0, -1, 1, 0, 0, 0),
                _ => Matrix.Identity
            };
            if (!matrix.IsIdentity)
            {
                image = new TransformedBitmap(bitmap, new MatrixTransform(matrix));
                image.Freeze();
            }
            var dimensions = orientation is >= 5 and <= 8 ? $"{height:N0} × {width:N0}" : $"{width:N0} × {height:N0}";
            return new(image, dimensions + (decoder.Frames.Count > 1 ? " · first frame" : ""));
        }
        catch (FileNotFoundException) { return new(null, "File unavailable — reconnect the drive or refresh"); }
        catch (DirectoryNotFoundException) { return new(null, "Folder unavailable — reconnect the drive or refresh"); }
        catch (UnauthorizedAccessException) { return new(null, "Access denied"); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or COMException)
        { return new(null, "Cannot preview this image — it may be damaged or require an installed image codec"); }
    }

    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is not BitmapMetadata metadata) return 1;
            foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
                if (metadata.GetQuery(query) is ushort orientation) return orientation;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException or COMException)
        { /* Images without readable EXIF metadata can still be previewed. */ }
        return 1;
    }
}
