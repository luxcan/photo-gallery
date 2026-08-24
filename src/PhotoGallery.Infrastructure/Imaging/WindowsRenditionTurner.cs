using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Imaging;

/// <inheritdoc cref="IRenditionTurner"/>
/// <remarks>
/// Re-encodes both renditions, which loses a little quality each time. That is
/// acceptable here and nowhere else: these two files are derived, and preparing
/// the picture again rebuilds them from the original at full quality with the
/// recorded turn applied. The original itself is never opened.
/// </remarks>
public sealed class WindowsRenditionTurner : IRenditionTurner
{
    private readonly IThumbnailStore _store;

    public WindowsRenditionTurner(IThumbnailStore store) => _store = store;

    public TurnedRendition? Turn(string thumbnailName, int degrees)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailName);

        if (((degrees % 360) + 360) % 360 == 0)
        {
            return null;
        }

        // The preview first, because its size before the turn is the answer the
        // caller needs and a failure here means nothing should be recorded.
        if (TurnFile(_store.ResolvePreviewPath(thumbnailName),
                degrees, ThumbnailSizes.PreviewQuality) is not TurnedRendition preview)
        {
            return null;
        }

        // The tile is best effort. A grid cell showing the old way up until the
        // next pass is a blemish; refusing the whole turn over it is worse.
        TurnFile(_store.ResolveTilePath(thumbnailName), degrees, ThumbnailSizes.TileQuality);

        return preview;
    }

    /// <summary>
    /// Turns one file in place, reporting the size it was before.
    /// </summary>
    /// <remarks>
    /// Read whole into memory before anything is written, because the source and
    /// the destination are the same path. Decoding straight from the file and
    /// encoding back over it truncates the picture being read.
    /// </remarks>
    private static TurnedRendition? TurnFile(string path, int degrees, int quality)
    {
        try
        {
            byte[] original = File.ReadAllBytes(path);

            using var source = new MemoryStream(original, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = source;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            var before = new TurnedRendition(image.PixelWidth, image.PixelHeight);

            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(
                WindowsThumbnailGenerator.Turn(image, degrees)));

            using var turned = new MemoryStream();
            encoder.Save(turned);
            File.WriteAllBytes(path, turned.ToArray());

            return before;
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or NotSupportedException
                                      or ArgumentException
                                      or FormatException
                                      or COMException)
        {
            // A missing, locked or corrupt rendition. The row is left alone, so
            // the next preparation pass rebuilds it from the original.
            return null;
        }
    }
}
