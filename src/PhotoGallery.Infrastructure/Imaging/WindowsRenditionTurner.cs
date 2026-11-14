using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Imaging;

/// <inheritdoc cref="IRenditionTurner"/>
/// <remarks>
/// Re-encodes both renditions, which loses a little quality each time. That is
/// acceptable here and nowhere else: these two files are derived, and preparing
/// the picture again rebuilds them from the original at full quality with the
/// recorded turn applied. The original is never written, and read only to ask
/// whether the renditions still show it the right way up.
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
    /// The most bits two hashes of one picture may differ by and still be read as
    /// the same way up.
    /// </summary>
    private const int SameWayUp = 12;

    /// <summary>
    /// How much nearer the best quarter must be than the next, before it is
    /// trusted. A picture that looks alike every way round - sky, a wall - is
    /// left as it is.
    /// </summary>
    private const int ClearMargin = 8;

    public int? DegreesBehindOriginal(string thumbnailName, string originalPath, int rotation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailName);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        // Settled from the name, before anything is read. A video holds no tag
        // this app could have written, so the answer is always "not behind" -
        // and reading it first to find that out pulled the whole clip, hundreds
        // of megabytes, over the network for every video turn a sync brought in.
        if (MediaFileTypes.Classify(originalPath) == AssetKind.Video)
        {
            return 0;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(originalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        PerceptualHash original;
        try
        {
            // Not a picture the decoder reads - a video, or HEIC on a machine
            // without the extension - is a question with no answer rather than
            // one to ask again: its file holds no tag this app could have
            // written, so the turn it came with is all there is to apply.
            if (WindowsThumbnailGenerator.Upright(bytes, ThumbnailSizes.TileEdge, rotation)
                is not BitmapSource upright)
            {
                return 0;
            }

            original = WindowsThumbnailGenerator.ComputeHash(upright);
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return 0;
        }

        BitmapSource cached;
        try
        {
            cached = Load(File.ReadAllBytes(_store.ResolveTilePath(thumbnailName)));
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            // Nothing here to be behind. Turning it is then refused as well, and
            // that is what holds the answer until a picture exists.
            return 0;
        }

        (int Degrees, int Distance)[] ranked =
        [
            .. new[] { 0, 90, 180, 270 }
                .Select(degrees => (degrees, WindowsThumbnailGenerator
                    .ComputeHash(WindowsThumbnailGenerator.Turn(cached, degrees))
                    .DistanceTo(original)))
                .OrderBy(candidate => candidate.Item2),
        ];

        return ranked[0].Distance <= SameWayUp
               && ranked[1].Distance - ranked[0].Distance >= ClearMargin
            ? ranked[0].Degrees
            : 0;
    }

    private static bool IsUnreadable(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or InvalidOperationException
            or FormatException
            or FileFormatException
            or OutOfMemoryException
            or COMException;

    private static BitmapImage Load(byte[] bytes)
    {
        using var source = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = source;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
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
            BitmapImage image = Load(File.ReadAllBytes(path));

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
