using System.IO;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Reads GPS coordinates from an original's EXIF, without decoding it.
/// </summary>
/// <remarks>
/// The tag lookup is <see cref="GpsCoordinates"/> and the query paths are
/// <see cref="ExifQueries"/>, both already used by the preparing pass and both
/// already handling the HEIC layout and the southern and western hemispheres.
/// The only thing this adds is opening the file cheaply, which is why it is a
/// small class rather than a second copy of anything.
/// </remarks>
public sealed class ExifOriginalCoordinates : IOriginalCoordinates
{
    public CoordinateReading Read(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        try
        {
            // FileShare.Read so a file the user happens to have open elsewhere
            // is still readable, and DelayCreation so the decoder parses the
            // header and stops. The metadata has to be read inside the using:
            // with no cache the frame is still backed by this stream.
            using FileStream file = File.Open(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            BitmapFrame frame = BitmapFrame.Create(
                file, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);

            (double Latitude, double Longitude)? where = GpsCoordinates.From(
                ExifMetadata.Read(frame, ExifQueries.Latitude),
                ExifMetadata.Read(frame, ExifQueries.LatitudeRef),
                ExifMetadata.Read(frame, ExifQueries.Longitude),
                ExifMetadata.Read(frame, ExifQueries.LongitudeRef));

            return where is (double latitude, double longitude)
                ? CoordinateReading.At(latitude, longitude)
                : CoordinateReading.None;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException
                                       or OverflowException or InvalidOperationException
                                       or FileFormatException or OutOfMemoryException)
        {
            // The same list the preparing pass catches, and for the same reasons:
            // corrupt, truncated, a codec that is not installed, a share that
            // went away mid-read, or a OneDrive placeholder that is not really
            // here. None of them is an answer, so none of them is recorded.
            return CoordinateReading.Unreadable;
        }
    }
}
