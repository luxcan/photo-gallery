using System.Buffers;
using System.IO;
using PhotoGallery.App.Shell;

namespace PhotoGallery.App.Gallery;

/// <summary>
/// Pulls the ends of a video into Windows' file cache before anyone asks to
/// watch it.
/// </summary>
/// <remarks>
/// The wait before a clip appears is not the transfer - playback is already
/// streamed, and starts long before the file is through. It is the open: the
/// player reads the header, and for an MP4 or MOV recorded on a phone that
/// header often sits at the <em>end</em> of the file, so the very first thing it
/// does is seek across the whole clip. Over a 6.4 MB/s share that measured 2.6
/// seconds, every time, after the user had already asked.
///
/// <para>So the ends are read here instead, on a background thread, the moment
/// the picture is opened - while the poster is on screen and nobody is waiting.
/// Nothing is stored: the bytes go into a discarded buffer, and what makes it
/// work is that Windows keeps them in its own file cache, so the player's reads
/// are answered from memory rather than from the share.</para>
///
/// <para>Deliberately not a copy of the file. The library is 267 GB of video and
/// this app never duplicates an original; a few megabytes from each end of a
/// clip somebody is looking at is a different thing entirely, and it is bounded
/// by what they open rather than by what they own.</para>
/// </remarks>
internal static class VideoPrefetch
{
    /// <summary>
    /// How much of the front to pull in.
    /// </summary>
    /// <remarks>
    /// Enough for a header at the front plus the first seconds of picture, which
    /// is all the player needs to show something. Larger would spend the user's
    /// link on a clip they may not play.
    /// </remarks>
    private const int HeadBytes = 4 * 1024 * 1024;

    /// <summary>
    /// How much of the tail.
    /// </summary>
    /// <remarks>
    /// Where a phone leaves the index it wrote after recording finished. Small,
    /// because it is a table of offsets rather than picture.
    /// </remarks>
    private const int TailBytes = 512 * 1024;

    private const int ChunkBytes = 256 * 1024;

    /// <summary>
    /// Reads the ends of <paramref name="path"/> and throws the bytes away.
    /// </summary>
    /// <remarks>
    /// Every failure is swallowed on purpose. This is an optimisation nobody
    /// asked for and its worst outcome must be that playing the clip is as slow
    /// as it used to be - never that opening a picture reports an error about a
    /// file the user has not tried to watch.
    /// </remarks>
    public static async Task WarmAsync(string path, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);

        try
        {
            await using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ChunkBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await ReadAsync(file, 0, HeadBytes, buffer, cancellationToken).ConfigureAwait(false);

            // The tail only where the file is big enough for it to be somewhere
            // the head did not already cover.
            long tailFrom = file.Length - TailBytes;
            if (tailFrom > HeadBytes)
            {
                await ReadAsync(file, tailFrom, TailBytes, buffer, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The user moved on. Nothing to say about it.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            DiagnosticLog.Write($"could not warm {Path.GetFileName(path)}", ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task ReadAsync(
        FileStream file, long from, long count, byte[] buffer, CancellationToken cancellationToken)
    {
        file.Position = from;

        for (long read = 0; read < count;)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int wanted = (int)Math.Min(buffer.Length, count - read);
            int got = await file.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken)
                .ConfigureAwait(false);

            if (got == 0)
            {
                return;
            }

            read += got;
        }
    }
}
