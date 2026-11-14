using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Reads a cached preview into plain blue-green-red bytes.
/// </summary>
/// <remarks>
/// One reader for every graph that looks at a preview - faces, content search
/// and eyes - because they must all see the same pixels. Two private copies of
/// this already existed when the third reader arrived.
/// </remarks>
public static class PreviewPixels
{
    /// <summary>
    /// The preview's pixels, or null when it will not open.
    /// </summary>
    /// <remarks>
    /// No orientation or metadata handling: a preview was written by this app
    /// from an already-rotated bitmap, so what is on disk is what should be
    /// looked at. A preview that will not decode is reported as unread rather
    /// than as a picture of nothing, so the caller can tell the two apart.
    /// </remarks>
    public static BgrImage? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using FileStream stream =
                new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.StreamSource = stream;
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.EndInit();
            decoded.Freeze();

            var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgr24, null, 0d);
            converted.Freeze();

            if (converted.PixelWidth <= 0 || converted.PixelHeight <= 0)
            {
                return null;
            }

            int stride = converted.PixelWidth * BgrImage.BytesPerPixel;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            return new BgrImage(pixels, converted.PixelWidth, converted.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException
                                       or OverflowException or InvalidOperationException
                                       or FileFormatException or OutOfMemoryException)
        {
            return null;
        }
    }
}
