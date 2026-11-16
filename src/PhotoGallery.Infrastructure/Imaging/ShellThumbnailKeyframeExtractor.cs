using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Takes a video's poster from the same thumbnail Explorer shows for it.
/// </summary>
/// <remarks>
/// The cheapest of the three routes [08] weighs up, and the only one that needs
/// nothing bundled and no codec knowledge of its own: whatever the machine can
/// already show a thumbnail for - AVCHD, Matroska, anything a codec pack has
/// registered a handler for - this can take a poster from, because it is asking
/// the same component Explorer asks.
///
/// <para><strong>One frame, not three.</strong> The shell will only ever give the
/// picture it has decided represents the file, so this satisfies the poster and
/// gives the face pass one frame to read. Finding the people who appear later in
/// a clip needs seeking, which means Media Foundation or FFmpeg behind this same
/// port - and that is why <see cref="IKeyframeExtractor"/> returns a list rather
/// than an image.</para>
///
/// <para>Duration is left null here for the same reason. It is not free from the
/// shell without walking the property system, and the extractor that seeks gets
/// it from the container header as part of an open it is making anyway.</para>
/// </remarks>
public sealed class ShellThumbnailKeyframeExtractor : IKeyframeExtractor
{
    /// <summary>
    /// What is asked of the shell, which is the larger rendition's edge.
    /// </summary>
    /// <remarks>
    /// The shell treats this as a request rather than an instruction and returns
    /// what it has, so a smaller thumbnail is a normal answer and is used as it
    /// comes - <see cref="Renditions.Scale"/> never enlarges. Asking for the
    /// preview size rather than the tile size means the face detector gets the
    /// most pixels the machine is willing to give it.
    /// </remarks>
    private const int RequestedEdge = ThumbnailSizes.PreviewEdge;

    /// <summary>
    /// How many times a clip is asked for before it is called undecodable.
    /// </summary>
    /// <remarks>
    /// Measured on the real library rather than chosen: the pass wrote off 24
    /// videos in 468, and of six checked by hand five gave a poster immediately
    /// on the next attempt. The shell fails intermittently against a file on a
    /// network share - and the failure looks exactly like the one a container
    /// with no codec gives, so the only way to tell them apart is to ask again.
    ///
    /// <para>Three costs nothing where it is not needed. A genuinely undecodable
    /// container fails fast, so two extra attempts on the handful that are
    /// really dead is a rounding error against a pass measured in tens of
    /// minutes - and being wrong the other way means a clip stays blank for
    /// good.</para>
    /// </remarks>
    private const int Attempts = 3;

    /// <summary>How long to leave the share alone before asking again.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long one clip's thumbnail handler is given before it is given up
    /// on.
    /// </summary>
    /// <remarks>
    /// The handler is someone else's code, and a clip that makes it hang would
    /// otherwise hold the pass - and every phase after it, Sync included - for
    /// as long as the app stays open, on every scan. A minute is generous on
    /// purpose, but it is not proof of a hung handler: a share that stalls - the
    /// Wi-Fi dropping, the disks spinning up - is just as slow, which is why a
    /// timeout is only believed once the file has been seen again quickly.
    /// </remarks>
    private static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the share is given to say whether the file is still there.
    /// </summary>
    /// <remarks>
    /// Asking that of a healthy share takes milliseconds. One that cannot answer
    /// in a few seconds is the share in trouble, not the clip.
    /// </remarks>
    private static readonly TimeSpan ReachableTimeout = TimeSpan.FromSeconds(5);

    public async Task<KeyframeReading> ExtractAsync(
        string originalPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        cancellationToken.ThrowIfCancellationRequested();

        for (int attempt = 1; ; attempt++)
        {
            KeyframeReading reading;
            try
            {
                reading = await OnStaThreadAsync(
                    () => Extract(originalPath), HandlerTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Not asked again either way. A hung handler would hang again,
                // and a stalled share would cost another minute per worker.
                return await WriteOffIfReachableAsync(
                    () => Reachable(originalPath), ReachableTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Extracted is done, and Undecodable here means something that
            // cannot come right by asking again - an empty file, or one that is
            // no longer there at all.
            if (reading.IsSettled)
            {
                return reading;
            }

            if (attempt >= Attempts)
            {
                // The last attempt's doubt has to be resolved one way or the
                // other. Undecodable is the answer that stops the file being
                // opened on every future run, and after three tries against a
                // reachable file it is the honest one.
                return await WriteOffIfReachableAsync(
                    () => Reachable(originalPath), ReachableTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Undecodable when the file answers quickly that it is there; otherwise
    /// Unavailable, so the clip is offered again once the share is back.
    /// </summary>
    /// <remarks>
    /// The check is bounded like the handler, because asking a stalled share
    /// whether a file exists can hang just as long. Writing a clip off is
    /// permanent - it is never offered again - so only a share that is plainly
    /// healthy may do it. A share that is slow to answer, or says the file has
    /// gone, leaves the clip unsettled, and the pass then asks whether the
    /// whole source has gone.
    /// </remarks>
    internal static async Task<KeyframeReading> WriteOffIfReachableAsync(
        Func<bool> reachable, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await OnStaThreadAsync(reachable, timeout, cancellationToken).ConfigureAwait(false)
                ? KeyframeReading.Undecodable
                : KeyframeReading.Unavailable;
        }
        catch (TimeoutException)
        {
            return KeyframeReading.Unavailable;
        }
    }

    /// <summary>
    /// Whether the file itself can still be seen, which is what separates a
    /// container that will not decode from a share that has gone.
    /// </summary>
    private static bool Reachable(string originalPath)
    {
        try
        {
            return File.Exists(originalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static KeyframeReading Extract(string originalPath)
    {
        nint bitmap = 0;
        try
        {
            // An empty file is the one failure that needs no second opinion: it
            // is reachable, it is nothing, and no decoder will ever make a
            // picture out of it. This library holds one.
            var file = new FileInfo(originalPath);
            if (!file.Exists)
            {
                return KeyframeReading.Unavailable;
            }

            if (file.Length == 0)
            {
                return KeyframeReading.Undecodable;
            }

            var itemId = typeof(IShellItemImageFactory).GUID;
            int created = SHCreateItemFromParsingName(
                originalPath, 0, ref itemId, out IShellItemImageFactory factory);

            if (created != 0 || factory is null)
            {
                // The path would not even resolve, which says nothing about
                // whether the container can be decoded.
                return KeyframeReading.Unavailable;
            }

            try
            {
                // ThumbnailOnly, so that a container with no handler fails here
                // rather than handing back the generic film icon. A row of
                // identical icons would look like the pass had worked.
                int got = factory.GetImage(
                    new ShellSize(RequestedEdge, RequestedEdge),
                    ShellImageFlags.ThumbnailOnly | ShellImageFlags.BiggerSizeOk,
                    out bitmap);

                if (got != 0 || bitmap == 0)
                {
                    // Deliberately not Undecodable. This is the failure a
                    // container with no codec gives - and it is also the one a
                    // network share gives when it blinks, which is why the
                    // caller asks again before believing it.
                    return KeyframeReading.Unavailable;
                }

                BitmapSource frame = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                frame.Freeze();

                BitmapSource preview = Renditions.Scale(frame, ThumbnailSizes.PreviewEdge);
                BitmapSource tile = Renditions.Scale(preview, ThumbnailSizes.TileEdge);

                return KeyframeReading.From(new ExtractedVideo(
                    Duration: null,
                    frame.PixelWidth,
                    frame.PixelHeight,
                    [
                        new ExtractedKeyframe(
                            TimeSpan.Zero,
                            Renditions.Encode(tile, ThumbnailSizes.TileQuality),
                            Renditions.Encode(preview, ThumbnailSizes.PreviewQuality)),
                    ]));
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex) when (ex is COMException or IOException
                                       or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or OutOfMemoryException)
        {
            // One bad file among thousands must not stop the pass - but nor may
            // it be written off on the strength of an exception that a share
            // going away throws just as readily as a broken container does.
            // Unsettled, so the caller asks again.
            return KeyframeReading.Unavailable;
        }
        finally
        {
            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }
        }
    }

    /// <summary>
    /// Runs one extraction on a thread of its own, in a single-threaded
    /// apartment.
    /// </summary>
    /// <remarks>
    /// Thumbnail handlers are third-party code - a codec pack's, a camera
    /// maker's - and a good many of them assume the apartment Explorer calls
    /// them from. The pass runs its work on the thread pool, which is
    /// multi-threaded, so without this the handlers that care would fail on some
    /// machines and not others.
    ///
    /// <para>A thread per video sounds extravagant and is not: this pass opens a
    /// few thousand files over the course of an hour, and each of them already
    /// costs a seek across the network.</para>
    ///
    /// <para>The wait is bounded, and Stop ends it. A handler that never
    /// returns cannot be made to, so its thread is left behind - a background
    /// thread, so it does not keep the app open - and the caller is told with a
    /// <see cref="TimeoutException"/>, because what a timeout means depends on
    /// whether the share is still answering.</para>
    /// </remarks>
    internal static Task<T> OnStaThreadAsync<T>(
        Func<T> work, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                result.TrySetResult(work());
            }
            catch (Exception ex)
            {
                // Handed to the waiting caller exactly as a plain call would
                // have thrown it, rather than ending the process from a thread
                // nobody is watching.
                result.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return result.Task.WaitAsync(timeout, cancellationToken);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path, nint bindContext, ref Guid riid, out IShellItemImageFactory item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(ShellSize size, ShellImageFlags flags, out nint bitmap);
    }

    /// <summary>The shell's own SIZE, which is two 32-bit pixel counts.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ShellSize
    {
        private readonly int _width;
        private readonly int _height;

        public ShellSize(int width, int height)
        {
            _width = width;
            _height = height;
        }
    }

    [Flags]
    private enum ShellImageFlags
    {
        /// <summary>Return a larger thumbnail rather than scaling one up.</summary>
        BiggerSizeOk = 0x00000001,

        /// <summary>Fail rather than falling back to the file type's icon.</summary>
        ThumbnailOnly = 0x00000008,
    }
}
