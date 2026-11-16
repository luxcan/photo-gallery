using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
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
    /// than as a picture of nothing, so the caller can tell the two apart -
    /// and so is one whose header claims a size no preview has.
    /// </remarks>
    public static BgrImage? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using FileStream stream =
                new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            if (!IsPreviewSized(stream))
            {
                return null;
            }

            stream.Position = 0;

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
        catch (Exception ex) when (DecodeFailure.IsUnreadable(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// The longest edge a preview is believed to have before it is decoded.
    /// </summary>
    /// <remarks>
    /// Every preview this app writes is at most <see cref="ThumbnailSizes.PreviewEdge"/>
    /// on its longer side, but a preview can also arrive from the Sync folder,
    /// copied byte for byte from another device. A small file there can claim
    /// tens of thousands of pixels a side, and decoding it whole costs gigabytes
    /// - on every faces and content pass, several at once. Four times the edge
    /// leaves room for any preview an earlier build might have written while
    /// refusing anything no build ever has.
    /// </remarks>
    internal const int LargestEdge = ThumbnailSizes.PreviewEdge * 4;

    /// <summary>
    /// Whether the picture's header gives a size a preview could have, read
    /// without decoding a single pixel.
    /// </summary>
    private static bool IsPreviewSized(Stream stream)
    {
        BitmapFrame header = BitmapFrame.Create(
            stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);

        return header.PixelWidth is > 0 and <= LargestEdge
               && header.PixelHeight is > 0 and <= LargestEdge;
    }
}
