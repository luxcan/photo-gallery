using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Faces;

/// <inheritdoc cref="IFaceScanner"/>
public sealed class OnnxFaceScanner : IFaceScanner, IDisposable
{
    /// <summary>
    /// Faces shorter than this on their short edge are not recorded.
    /// </summary>
    /// <remarks>
    /// Measured on this library: below roughly this size the detector is
    /// returning the blurred and half-turned fragments in the background of
    /// group shots. They are real faces and they are useless - too coarse to
    /// recognise, and numerous enough to bury the people worth naming under a
    /// tail of one-off groups.
    /// </remarks>
    public const int MinimumFaceEdge = 32;

    private readonly IModelStore _models;
    private readonly Lock _gate = new();

    /// <summary>
    /// Held while a scan is running, and taken exclusively to dispose.
    /// </summary>
    /// <remarks>
    /// A native inference session freed while a thread is inside <c>Run</c> is an
    /// access violation, not an exception - the process simply goes, leaving
    /// nothing to read afterwards. This scanner is a singleton, so the container
    /// disposes it when the window closes, which is exactly when a twenty minute
    /// pass is most likely to be running on eleven other threads.
    ///
    /// <para>A reader-writer lock rather than a plain one because the whole point
    /// of the pass is that scans run at once: they take it shared and do not wait
    /// for each other, and disposal waits for all of them. Never disposed itself
    /// - a scan arriving after the scanner has gone should meet the
    /// <see cref="ObjectDisposedException"/> below rather than one raised by the
    /// lock it was trying to take.</para>
    /// </remarks>
    private readonly ReaderWriterLockSlim _inUse = new();

    private ScrfdFaceDetector? _detector;
    private ArcFaceEmbedder? _embedder;
    private bool _disposed;

    public OnnxFaceScanner(IModelStore models) => _models = models;

    public Task<IReadOnlyList<ScannedFace>?> ScanAsync(
        string previewPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewPath);

        // Wholly synchronous, processor-bound work: decoding, then two graphs.
        // Left on the calling thread it would hold whatever asked for it, and
        // the pass runs several of these at once by design.
        return Task.Run<IReadOnlyList<ScannedFace>?>(
            () => Scan(previewPath, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<ScannedFace>? Scan(string previewPath, CancellationToken cancellationToken)
    {
        BgrImage? preview = Decode(previewPath);
        if (preview is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Held for the whole of the inference, not merely while the sessions are
        // fetched: what must not happen is the graphs being freed between here
        // and the last Embed below.
        _inUse.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            (ScrfdFaceDetector detector, ArcFaceEmbedder embedder) = Graphs();
            var found = new List<ScannedFace>();

            foreach (DetectedFace face in detector.Detect(preview))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (face.ShortEdge < MinimumFaceEdge)
                {
                    continue;
                }

                BgrImage? aligned = FaceAligner.Align(preview, face.Landmarks);
                if (aligned is null)
                {
                    // Landmarks that cannot be fitted are not a face worth keeping.
                    continue;
                }

                FaceEmbedding embedding = embedder.Embed(aligned);
                found.Add(new ScannedFace(BoundsWithin(face, preview), face.Score, embedding));
            }

            return found;
        }
        finally
        {
            _inUse.ExitReadLock();
        }
    }

    /// <summary>
    /// The detected box as whole pixels inside the preview.
    /// </summary>
    /// <remarks>
    /// Clamped because the model is free to predict a box running off the edge -
    /// it often does for a face at the side of a photograph - and a crop
    /// rectangle that leaves the picture cannot be drawn or cut from.
    /// </remarks>
    private static FaceBounds BoundsWithin(DetectedFace face, BgrImage preview)
    {
        int left = Math.Clamp((int)MathF.Round(face.Left), 0, preview.Width - 1);
        int top = Math.Clamp((int)MathF.Round(face.Top), 0, preview.Height - 1);
        int right = Math.Clamp((int)MathF.Round(face.Right), left + 1, preview.Width);
        int bottom = Math.Clamp((int)MathF.Round(face.Bottom), top + 1, preview.Height);

        return new FaceBounds(left, top, right - left, bottom - top);
    }

    private (ScrfdFaceDetector Detector, ArcFaceEmbedder Embedder) Graphs()
    {
        // Built once and kept. Loading the recognition graph reads 166 MB, and
        // the sessions are safe to run from several threads at once, so there is
        // nothing to gain from a shorter life than the scanner's own.
        lock (_gate)
        {
            _detector ??= new ScrfdFaceDetector(Open(ModelId.FaceDetection));
            _embedder ??= new ArcFaceEmbedder(Open(ModelId.FaceRecognition));

            return (_detector, _embedder);
        }
    }

    private InferenceSession Open(ModelId id)
    {
        ModelState state = _models.StateOf(id);
        if (state != ModelState.Ready)
        {
            throw new InvalidOperationException(
                $"The {id} model is {state}. It has to be installed before faces can be found.");
        }

        return new InferenceSession(_models.ResolvePath(id), CreateOptions());
    }

    /// <summary>
    /// One thread per graph.
    /// </summary>
    /// <remarks>
    /// The pass already runs several photographs at once, and letting each
    /// session spread over every core as well would leave the machine competing
    /// with itself. Parallelism belongs to whoever is holding the work list.
    /// </remarks>
    private static SessionOptions CreateOptions() => new()
    {
        IntraOpNumThreads = 1,
        InterOpNumThreads = 1,
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
    };

    /// <summary>
    /// The preview as plain blue-green-red bytes, or null when it will not open.
    /// </summary>
    /// <remarks>
    /// No orientation or metadata handling: a preview was written by this app
    /// from an already-rotated bitmap, so what is on disk is what should be
    /// looked at.
    /// </remarks>
    private static BgrImage? Decode(string path)
    {
        try
        {
            using FileStream stream =
                new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.StreamSource = stream;
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.EndInit();
            decoded.Freeze();

            var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgr24, null, 0d);
            converted.Freeze();

            if (converted.PixelWidth <= 0 || converted.PixelHeight <= 0)
            {
                return null;
            }

            int stride = converted.PixelWidth * BgrImage.BytesPerPixel;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);

            return new BgrImage(pixels, converted.PixelWidth, converted.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException
                                       or OverflowException or InvalidOperationException
                                       or FileFormatException or OutOfMemoryException)
        {
            // A preview that will not decode is reported as unread rather than
            // as a photograph with no faces in it.
            return null;
        }
    }

    /// <summary>
    /// Waits for the scans already running, then frees the graphs.
    /// </summary>
    /// <remarks>
    /// Closing the window during a pass blocks for as long as the previews still
    /// in flight take - a fraction of a second each. That is the cost of not
    /// pulling a model out from under a thread that is reading it.
    /// </remarks>
    public void Dispose()
    {
        _inUse.EnterWriteLock();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _detector?.Dispose();
            _embedder?.Dispose();
        }
        finally
        {
            _inUse.ExitWriteLock();
        }
    }
}
