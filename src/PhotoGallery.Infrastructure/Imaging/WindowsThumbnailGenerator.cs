using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Builds thumbnails with the imaging codecs Windows already provides.
/// </summary>
/// <remarks>
/// The file is read once into memory and decoded from there, because decoding
/// straight from a network share issues many small reads and is far slower than
/// one sequential fetch.
///
/// <para><c>DecodePixelWidth</c> makes the codec produce the reduced image
/// directly - for JPEG that is a scaled DCT decode, so a 4000px original costs a
/// fraction of a full decode. That one setting is the difference between a pass
/// measured in minutes and one measured in hours.</para>
///
/// <para>Both renditions come from the same decode: the preview is produced at
/// its own size, and the tile is scaled down from it rather than decoding the
/// original twice.</para>
///
/// <para>The capture date and the perceptual hash are taken from that same
/// decode. Collecting them later would mean a second pass over 25 GB of
/// originals - an hour over the share - to learn things that were already in
/// hand.</para>
/// </remarks>
public sealed class WindowsThumbnailGenerator : IThumbnailGenerator
{
    public Task<GeneratedThumbnail?> GenerateAsync(
        string originalPath,
        int rotation = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        // Decoding is CPU-bound and synchronous; running it on a worker lets the
        // caller have many in flight at once.
        return Task.Run(
            () => Generate(originalPath, rotation, cancellationToken), cancellationToken);
    }

    private static GeneratedThumbnail? Generate(
        string originalPath, int rotation, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(originalPath);
            cancellationToken.ThrowIfCancellationRequested();

            // Hashed here because this is the one moment the bytes are in
            // memory. Doing it later would mean reading 25 GB a second time.
            string contentHash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            // A header-only pass, so the true size is known before choosing a
            // decode size.
            using var probeStream = new MemoryStream(bytes, writable: false);
            BitmapFrame probe = BitmapFrame.Create(
                probeStream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);

            int sourceWidth = probe.PixelWidth;
            int sourceHeight = probe.PixelHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return null;
            }

            int orientation = ReadOrientation(probe);
            DateTime? takenUtc = ReadTakenUtc(probe);
            (double Latitude, double Longitude)? where = ReadCoordinates(probe);

            BitmapSource preview = Decode(
                bytes, sourceWidth, sourceHeight, ThumbnailSizes.PreviewEdge);
            preview = ApplyOrientation(preview, orientation);

            // The user's own turn, on top of whatever the file said. Applied
            // while the picture is decoded rather than by reading the rendition
            // back afterwards, which would encode it a second time.
            preview = Turn(preview, rotation);
            cancellationToken.ThrowIfCancellationRequested();

            BitmapSource tile = Renditions.Scale(preview, ThumbnailSizes.TileEdge);

            // A quarter-turn swaps the picture's dimensions. Recording the
            // sensor's raw numbers would report a portrait photo as landscape,
            // because the rotation lives in EXIF rather than in the pixels.
            bool quarterTurned = orientation is 6 or 8
                                 ^ (((rotation % 360) + 360) % 360) is 90 or 270;

            return new GeneratedThumbnail(
                Renditions.Encode(tile, ThumbnailSizes.TileQuality),
                Renditions.Encode(preview, ThumbnailSizes.PreviewQuality),
                quarterTurned ? sourceHeight : sourceWidth,
                quarterTurned ? sourceWidth : sourceHeight,
                takenUtc,
                ComputeHash(tile),
                contentHash,
                where?.Latitude,
                where?.Longitude);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException
                                       or OverflowException or InvalidOperationException
                                       or FileFormatException or OutOfMemoryException)
        {
            // Corrupt, truncated, or a format with no codec installed - HEIC on
            // a machine without the HEIF extension lands here.
            return null;
        }
    }

    private static BitmapSource Decode(byte[] bytes, int width, int height, int maxEdge)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;

        // Constrain the longer edge and let the decoder scale the other.
        if (width >= height)
        {
            image.DecodePixelWidth = Math.Min(maxEdge, width);
        }
        else
        {
            image.DecodePixelHeight = Math.Min(maxEdge, height);
        }

        image.EndInit();
        image.Freeze();
        return image;
    }


    /// <summary>
    /// The perceptual hash, from the pixels already in memory.
    /// </summary>
    /// <remarks>
    /// Taken from the tile rather than the preview: it is the smaller of the two
    /// and both are scalings of one decode, so they hash alike. The hash is
    /// computed after orientation has been applied, so a photo and its rotated
    /// copy do not read as different pictures.
    /// </remarks>
    private static PerceptualHash ComputeHash(BitmapSource image)
    {
        var greyscale = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
        greyscale.Freeze();

        int width = greyscale.PixelWidth;
        int height = greyscale.PixelHeight;

        // Gray8 is one byte per pixel, so the stride is the width.
        byte[] pixels = new byte[width * height];
        greyscale.CopyPixels(pixels, width, 0);

        return PerceptualHash.FromGreyscale(pixels, width, height);
    }

    /// <summary>
    /// EXIF <c>DateTimeOriginal</c> - when the shutter fired, as opposed to when
    /// the file was last written.
    /// </summary>
    /// <remarks>
    /// Measured on this library, the file's own dates are poor substitutes: its
    /// creation date is the day it was copied to the share, which collapsed
    /// 3,000 photos spanning eight years onto thirteen days. This is the only
    /// honest date a photo carries, and 89% of them carry it.
    ///
    /// <para>EXIF records no time zone, so the value is wall-clock time as the
    /// camera saw it and is stored unconverted. Pretending to know the offset
    /// would be worse than admitting there is none.</para>
    /// </remarks>
    private static DateTime? ReadTakenUtc(BitmapFrame frame)
    {
        if (ReadMetadata(frame, ExifQueries.DateTaken) is not string text)
        {
            return null;
        }

        return DateTime.TryParseExact(
            text.Trim().TrimEnd('\0'),
            ExifDateFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime taken)
            ? taken
            : null;
    }

    /// <summary>
    /// Where the photograph was taken, from the GPS block of the same metadata.
    /// </summary>
    /// <remarks>
    /// Read here for the reason everything else on this pass is: the file is
    /// already open and already decoded, and collecting coordinates later would
    /// mean an hour over the share to learn something that was in hand.
    ///
    /// <para>Measured on this library, 39% of photographs carry coordinates.
    /// The other 61% are not a failure and are never retried - a camera without
    /// a receiver will not grow one.</para>
    ///
    /// <para>The GPS block sits under the same container split as the date and
    /// the orientation, so it is read through the same helper and the same
    /// per-tag lists. Asking only the JPEG path here would lose every HEIC
    /// photograph its location exactly as it once lost them their dates - and
    /// silently, because a photograph with no coordinates looks precisely like a
    /// camera that never had GPS.</para>
    /// </remarks>
    private static (double Latitude, double Longitude)? ReadCoordinates(BitmapFrame frame) =>
        GpsCoordinates.From(
            ReadMetadata(frame, ExifQueries.Latitude),
            ReadMetadata(frame, ExifQueries.LatitudeRef),
            ReadMetadata(frame, ExifQueries.Longitude),
            ReadMetadata(frame, ExifQueries.LongitudeRef));

    /// <summary>Reads the first of several equivalent metadata locations that is present.</summary>
    private static object? ReadMetadata(BitmapFrame frame, string[] queries) =>
        ExifMetadata.Read(frame, queries);

    /// <summary>EXIF spells dates with colons in the date part: 2015:06:03 14:22:07.</summary>
    private const string ExifDateFormat = "yyyy:MM:dd HH:mm:ss";

    /// <summary>
    /// EXIF orientation, so photos taken on a rotated phone are stored upright
    /// rather than leaving every viewer to correct them.
    /// </summary>
    private static int ReadOrientation(BitmapFrame frame) =>
        ReadMetadata(frame, ExifQueries.Orientation) is ushort value ? value : 1;

    /// <summary>A quarter turn clockwise, for a picture whose file did not say.</summary>
    public static BitmapSource Turn(BitmapSource image, int degrees)
    {
        ArgumentNullException.ThrowIfNull(image);

        int turn = (((degrees % 360) + 360) % 360);
        if (turn == 0)
        {
            return image;
        }

        var turned = new TransformedBitmap(image, new RotateTransform(turn));
        turned.Freeze();
        return turned;
    }

    private static BitmapSource ApplyOrientation(BitmapSource image, int orientation)
    {
        Transform? transform = orientation switch
        {
            3 => new RotateTransform(180),
            6 => new RotateTransform(90),
            8 => new RotateTransform(270),
            2 => new ScaleTransform(-1, 1),
            4 => new ScaleTransform(1, -1),
            _ => null,
        };

        if (transform is null)
        {
            return image;
        }

        var rotated = new TransformedBitmap(image, transform);
        rotated.Freeze();
        return rotated;
    }
}
