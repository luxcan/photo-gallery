namespace PhotoGallery.Infrastructure.Storage;

/// <summary>
/// Writes a file under a temporary name and renames it into place, so nothing
/// reading the real name ever finds half of one.
/// </summary>
/// <remarks>
/// The sync folder's pictures were written this way first, because two laptops
/// fetch the same missing picture at the same moment and a third must never read
/// half a JPEG. The pictures on this computer have the same problem for the same
/// reason - byte-identical photographs share one name, and a scan prepares
/// several photographs at once - so both now come through here rather than each
/// keeping its own copy of the dance.
///
/// <para>What a failure means is left to the caller, because it differs. The
/// sync folder answers false and lets the next scan copy the picture again; this
/// computer's store counts a name that is already there as done, since a name is
/// the picture's content. Either way the temporary is gone: whatever was written
/// to it is nobody's.</para>
///
/// <para>The temporary's name is the caller's too. Giving each write a name of
/// its own is what lets two writers of one picture run side by side instead of
/// one failing on the other's handle; the sync folder keeps the single name it
/// has always used, so nothing this build writes there changes.</para>
/// </remarks>
internal static class WholeFile
{
    /// <summary>
    /// Writes <paramref name="path"/> through <paramref name="temporary"/>,
    /// replacing whatever was there.
    /// </summary>
    /// <exception cref="IOException">The file could not be written or put in place.</exception>
    /// <exception cref="UnauthorizedAccessException">The same, refused rather than failed.</exception>
    public static async Task WriteAsync(
        string path,
        string temporary,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporary);
        ArgumentNullException.ThrowIfNull(write);

        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            // Stopped part way, refused, or out of room. Whatever is under the
            // real name is still whole, and the half-written temporary is
            // nobody's. Rethrown, because what the failure means is the
            // caller's to say.
            Forget(temporary);
            throw;
        }
    }

    private static void Forget(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover .tmp is untidy and harmless: nothing reads them, and
            // nothing counts them as a picture.
        }
    }
}
