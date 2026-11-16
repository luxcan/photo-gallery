using System.IO;
using System.Runtime.InteropServices;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// The exceptions that mean "this picture will not open", as opposed to a bug.
/// </summary>
/// <remarks>
/// One list for the readers that hand a file to the Windows imaging codecs and
/// write it off when it will not open. Each used to keep its own, and they
/// drifted: the turner and the orientation reader caught
/// <see cref="COMException"/> while the thumbnail, preview and coordinate
/// readers did not, and the turner missed a picture too large to hold. The
/// video poster reader keeps a list of its own on purpose, because a failure
/// there may be the share and is asked again rather than written off. WPF maps only the codec errors it
/// knows to .NET types and raises every other failure - an extension codec's
/// own error, a Media Foundation one - as a bare <see cref="COMException"/>. So
/// one file that upset a HEIF or raw codec in that way left the scan without
/// being written off, closed the app, and was opened again on the next scan.
///
/// <para>Corrupt, truncated, a codec that is not installed, a share that went
/// away mid-read, a OneDrive placeholder that is not really here, or a picture
/// too large to hold: all of them are a property of the file, not of the
/// code.</para>
/// </remarks>
internal static class DecodeFailure
{
    public static bool IsUnreadable(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or OverflowException
            or InvalidOperationException
            or FormatException
            or OutOfMemoryException
            or COMException;
}
