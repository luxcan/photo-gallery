using System.Windows.Media.Imaging;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Reading EXIF tags out of whatever container they arrived in.
/// </summary>
/// <remarks>
/// Containers disagree about where EXIF lives. JPEG puts it under the APP1
/// segment; HEIF exposes the same tags one level up. Measured on this library,
/// asking only the JPEG path left all 864 HEIC photos with no capture date and,
/// worse, unrotated - the orientation tag was being missed the same way,
/// silently, because a photo that is simply never turned looks like a photo that
/// was taken that way.
///
/// <para>Asking is the part that is shared. What to do with the answer is not:
/// the orientation writer needs to know <em>which</em> path held the tag so it
/// can write back to the same one, and it skips a path holding something that is
/// not an orientation, where a reader wants the first value present whatever it
/// is. So this exposes the guarded read and leaves the walk to each caller,
/// rather than forcing three policies through one loop.</para>
/// </remarks>
internal static class ExifMetadata
{
    /// <summary>
    /// The value at one query, or null when the container does not hold it there.
    /// </summary>
    /// <remarks>
    /// A container that does not understand a query throws rather than answering
    /// false, so the throw means the same thing as the absence and is treated the
    /// same way.
    /// </remarks>
    public static object? At(BitmapMetadata metadata, string query)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        try
        {
            return metadata.ContainsQuery(query) ? metadata.GetQuery(query) : null;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException
                                       or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The first of several equivalent locations that holds anything, or null
    /// when none of them does.
    /// </summary>
    public static object? Read(BitmapFrame frame, string[] queries)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(queries);

        if (frame.Metadata is not BitmapMetadata metadata)
        {
            return null;
        }

        foreach (string query in queries)
        {
            if (At(metadata, query) is object value)
            {
                return value;
            }
        }

        return null;
    }
}
