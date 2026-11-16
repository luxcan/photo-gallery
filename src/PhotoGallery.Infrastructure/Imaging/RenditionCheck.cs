using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Whether a file is plausibly one of this app's renditions, judged from its
/// length and its header alone.
/// </summary>
/// <remarks>
/// For a picture arriving from the sync folder, where a name proves nothing:
/// anybody who can write there can put any file under any name. This cannot
/// tell that a picture is of the right photograph - nothing short of reading
/// the original could - but it does keep out what would hurt: a file of any
/// size, something that is not a picture at all, and a picture whose pixels
/// would take the viewer and the face pass a gigabyte of memory to open.
///
/// <para>The header only, never the pixels. Every photograph a new laptop takes
/// from the folder passes through here, and the reason it takes them is that
/// copying is quicker than decoding.</para>
/// </remarks>
internal static class RenditionCheck
{
    /// <summary>
    /// Whether <paramref name="picture"/> is a JPEG no longer than
    /// <paramref name="maxBytes"/> and no wider or taller than
    /// <paramref name="maxEdge"/>.
    /// </summary>
    /// <remarks>
    /// Leaves the stream where it found it, so the same open file can then be
    /// copied: checking one handle and copying another would let the file
    /// change in between.
    /// </remarks>
    public static bool Fits(Stream picture, long maxBytes, int maxEdge)
    {
        ArgumentNullException.ThrowIfNull(picture);

        long start = picture.Position;

        try
        {
            if (picture.Length - start > maxBytes)
            {
                return false;
            }

            // Every rendition this app writes is a JPEG, so anything else under
            // a rendition's name was not written by it.
            BitmapDecoder decoder = BitmapDecoder.Create(
                picture,
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.None);

            if (decoder is not JpegBitmapDecoder || decoder.Frames.Count == 0)
            {
                return false;
            }

            BitmapFrame frame = decoder.Frames[0];

            return frame.PixelWidth is > 0 and var width
                && frame.PixelHeight is > 0 and var height
                && width <= maxEdge
                && height <= maxEdge;
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or NotSupportedException
                                      or ArgumentException
                                      or OverflowException
                                      or InvalidOperationException
                                      or FileFormatException
                                      or COMException)
        {
            return false;
        }
        finally
        {
            picture.Position = start;
        }
    }
}
