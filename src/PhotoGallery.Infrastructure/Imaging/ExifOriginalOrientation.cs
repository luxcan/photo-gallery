using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Imaging;

/// <inheritdoc cref="IOriginalOrientation"/>
public sealed class ExifOriginalOrientation : IOriginalOrientation
{
    /// <summary>
    /// The four upright orientations, in clockwise order.
    /// </summary>
    /// <remarks>
    /// A quarter turn is a step along this ring, so composing a turn with what
    /// the file already claims is an index and a modulus rather than a table of
    /// sixteen cases. The mirrored orientations - 2, 4, 5 and 7 - are absent on
    /// purpose: they are vanishingly rare, composing a rotation with a flip is
    /// easy to get subtly wrong, and being wrong here means quietly mangling
    /// somebody's photograph. A file carrying one is refused and corrected in
    /// the app's own copies instead.
    /// </remarks>
    private static readonly ushort[] Clockwise = [1, 6, 3, 8];

    public bool TryTurn(string fullPath, int degrees)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        int quarters = (((degrees / 90) % 4) + 4) % 4;
        if (quarters == 0 || degrees % 90 != 0)
        {
            return false;
        }

        DateTime created, written, read;
        try
        {
            created = File.GetCreationTimeUtc(fullPath);
            written = File.GetLastWriteTimeUtc(fullPath);
            read = File.GetLastAccessTimeUtc(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException)
        {
            return false;
        }

        bool turned = Write(fullPath, quarters);

        // Always, not only on success: opening a file for writing can move its
        // access time whether or not anything was written, and the whole point
        // of restoring them is that nothing about the file appears to change.
        //
        // This is not cosmetic. The gallery dates a photograph without EXIF by
        // the earlier of these two, and the scan decides a file has been
        // replaced by comparing them - so letting them move would both misplace
        // the picture in the grid and throw away everything derived from it,
        // including the names confirmed on its faces.
        Restore(fullPath, created, written, read);

        return turned;
    }

    private static bool Write(string fullPath, int quarters)
    {
        try
        {
            using FileStream file = File.Open(
                fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            BitmapDecoder decoder = BitmapDecoder.Create(
                file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);

            if (decoder.Frames.Count == 0
                || decoder.Frames[0].Metadata is not BitmapMetadata metadata)
            {
                return false;
            }

            if (Compose(metadata, quarters) is not (string query, ushort next))
            {
                return false;
            }

            InPlaceBitmapMetadataWriter writer =
                decoder.Frames[0].CreateInPlaceBitmapMetadataWriter();

            // Written back to the path it was read from. A HEIF file answered at
            // /ifd and setting /app1 would leave the real tag untouched while
            // reporting success.
            writer.SetQuery(query, next);

            // False when the header has no room to hold it. The file is left
            // alone rather than grown, which is what keeps this lossless.
            return writer.TrySave();
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or NotSupportedException
                                      or ArgumentException
                                      or InvalidOperationException
                                      or FormatException
                                      or COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// Where the tag is, and what it should say after the turn - or null when it
    /// cannot be said.
    /// </summary>
    /// <remarks>
    /// Every container this app reads is tried, because the same tag sits in a
    /// different place in each. Looking only where a JPEG keeps it meant a HEIC
    /// photograph, of which this library holds 864, was reported as having no
    /// orientation tag at all and its file was never corrected.
    /// </remarks>
    private static (string Query, ushort Value)? Compose(BitmapMetadata metadata, int quarters)
    {
        foreach (string query in ExifQueries.Orientation)
        {
            if (ExifMetadata.At(metadata, query) is not ushort claimed)
            {
                continue;
            }

            // Zero is not a legal orientation and some encoders write it anyway.
            // Read as upright, which is what applying it already does.
            int index = Array.IndexOf(Clockwise, claimed == 0 ? (ushort)1 : claimed);

            // A mirrored orientation, which this deliberately refuses rather than
            // composing a rotation with a flip. Not worth trying the next path:
            // the tag has been found, and the answer is no.
            return index < 0
                ? null
                : (query, Clockwise[(index + quarters) % Clockwise.Length]);
        }

        // No tag anywhere. Adding one needs room the file does not have, so this
        // is the case the app's own copies exist for.
        return null;
    }

    private static void Restore(string fullPath, DateTime created, DateTime written, DateTime read)
    {
        try
        {
            File.SetCreationTimeUtc(fullPath, created);
            File.SetLastWriteTimeUtc(fullPath, written);
            File.SetLastAccessTimeUtc(fullPath, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException)
        {
            // The tag is written and correct; the dates could not be put back.
            // The next scan will read the file as changed and rebuild what it
            // derived - slow and it costs that photo's names, but not silent:
            // the pass reports every picture it re-read.
        }
    }
}
