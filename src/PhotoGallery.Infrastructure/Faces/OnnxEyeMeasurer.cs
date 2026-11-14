using Microsoft.ML.OnnxRuntime;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Imaging;
using PhotoGallery.Infrastructure.Models;

namespace PhotoGallery.Infrastructure.Faces;

/// <inheritdoc cref="IEyeMeasurer"/>
/// <remarks>
/// Two graphs per preview. The detector runs again because the five points it
/// finds are not stored with a face, and they are what say which way the face
/// is pointing and which way is level; the stored box is matched to the
/// detection it came from by overlap. Then the 106-point landmark graph runs
/// once per face, on a crop turned level.
///
/// <para>The landmark file is proved against the manifest's length and digest
/// where it is found, rather than through the store, so the phase 1 harness can
/// point it at a folder it must not change.</para>
/// </remarks>
public sealed class OnnxEyeMeasurer : IEyeMeasurer, IDisposable
{
    /// <summary>
    /// How much a fresh detection has to overlap a stored box to be that face.
    /// </summary>
    /// <remarks>
    /// The stored box is the detector's own, rounded and clamped to the
    /// picture, on the same preview - so a true match overlaps almost
    /// completely (0.99 on the faces it was tried on). Half is far enough below
    /// that to survive the rounding and far enough above a neighbour's box in a
    /// group shot to never take somebody else's points.
    /// </remarks>
    public const float LeastOverlap = 0.5f;

    private readonly IModelStore _models;
    private readonly string? _landmarkModelPath;
    private readonly Lock _gate = new();

    /// <summary>Held while measuring, and taken exclusively to dispose.</summary>
    /// <remarks>
    /// The same guard as <see cref="OnnxFaceScanner"/>'s, for the same reason: a
    /// native session freed while a thread is inside <c>Run</c> is an access
    /// violation that nothing can catch.
    /// </remarks>
    private readonly ReaderWriterLockSlim _inUse = new();

    private ScrfdFaceDetector? _detector;
    private FaceLandmarker? _landmarker;
    private bool _disposed;

    /// <summary>
    /// A measurer that finds the landmark file where the store says it is when
    /// the graphs are first built, as it finds the detector - so a models folder
    /// moved while the app runs is followed.
    /// </summary>
    public OnnxEyeMeasurer(IModelStore models)
    {
        ArgumentNullException.ThrowIfNull(models);

        _models = models;
    }

    /// <summary>
    /// A measurer held to one landmark file, for the phase 1 harness, which
    /// must not go through a store that could change the folder it reads.
    /// </summary>
    public OnnxEyeMeasurer(IModelStore models, string landmarkModelPath)
        : this(models)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(landmarkModelPath);

        _landmarkModelPath = landmarkModelPath;
    }

    public Task<IReadOnlyList<EyeMeasurement>?> MeasureAsync(
        string previewPath,
        IReadOnlyList<FaceBounds> faces,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewPath);
        ArgumentNullException.ThrowIfNull(faces);

        // Wholly synchronous work, so it is moved off the caller's thread as
        // the face scan is.
        return Task.Run<IReadOnlyList<EyeMeasurement>?>(
            () =>
            {
                BgrImage? preview = PreviewPixels.Read(previewPath);
                if (preview is null)
                {
                    return null;
                }

                try
                {
                    return Measure(preview, faces, cancellationToken).Select(m => m.Measurement).ToList();
                }
                catch (OnnxRuntimeException)
                {
                    // The graphs would not load or run - a card reset, a device
                    // lost - which says nothing about these faces. Unknown would
                    // be kept as a reading and the photograph never measured
                    // again; nothing leaves it due for the next pass.
                    return null;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// One measurement per face, in the order given, with the points it was
    /// read from.
    /// </summary>
    /// <remarks>
    /// Public beside the port for the phase 1 measurement, which has already
    /// read the preview for its sharpness and draws the eye outlines to be
    /// checked by eye. A graph that will not load or run throws its
    /// <see cref="OnnxRuntimeException"/>, so that "could not run" is never
    /// mistaken for a face that ran and could not be told.
    /// </remarks>
    public IReadOnlyList<MeasuredEyes> Measure(
        BgrImage preview, IReadOnlyList<FaceBounds> faces, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(faces);

        if (faces.Count == 0)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return [];
        }

        // One graph run at a time on a card - a correctness limit, not a tuning
        // choice - and free on the processor.
        using InferenceDevice.Slot slot = InferenceDevice.Enter(cancellationToken);

        _inUse.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            (ScrfdFaceDetector detector, FaceLandmarker landmarker) = Graphs();
            IReadOnlyList<DetectedFace> detected = detector.Detect(preview);

            var measured = new List<MeasuredEyes>(faces.Count);
            foreach (FaceBounds face in faces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                measured.Add(MeasureOne(preview, face, detected, landmarker));
            }

            return measured;
        }
        finally
        {
            _inUse.ExitReadLock();
        }
    }

    private static MeasuredEyes MeasureOne(
        BgrImage preview, FaceBounds face, IReadOnlyList<DetectedFace> detected, FaceLandmarker landmarker)
    {
        DetectedFace? match = detected
            .Select(candidate => (Candidate: candidate, Overlap: Overlap(candidate, face)))
            .Where(pair => pair.Overlap >= LeastOverlap)
            .OrderByDescending(pair => pair.Overlap)
            .Select(pair => pair.Candidate)
            .FirstOrDefault();

        if (match is null)
        {
            // The detector no longer finds a face there - a preview rewritten
            // since, or a box drawn by hand - so there is no pose to trust.
            return new MeasuredEyes(EyeMeasurement.Unmeasured, null);
        }

        float roll = FacePose.Roll(match.Landmarks);
        float[]? points = float.IsFinite(roll) ? landmarker.Locate(preview, face, roll) : null;
        if (points is null)
        {
            return new MeasuredEyes(EyeMeasurement.Unmeasured, null);
        }

        var measurement = new EyeMeasurement(
            EyeOutline.Openness(points, EyeOutline.PictureLeft),
            EyeOutline.Openness(points, EyeOutline.PictureRight),
            FacePose.Turn(match.Landmarks),
            EyeOutline.Agreement(points, match.Landmarks));

        return new MeasuredEyes(measurement, points);
    }

    /// <summary>Intersection over union of a detection and a stored box.</summary>
    private static float Overlap(DetectedFace detected, FaceBounds box)
    {
        float width = MathF.Min(detected.Right, box.X + box.Width) - MathF.Max(detected.Left, box.X);
        float height = MathF.Min(detected.Bottom, box.Y + box.Height) - MathF.Max(detected.Top, box.Y);
        if (width <= 0f || height <= 0f)
        {
            return 0f;
        }

        float intersection = width * height;
        float union = (detected.Width * detected.Height) + box.Area - intersection;
        return union <= 0f ? 0f : intersection / union;
    }

    private (ScrfdFaceDetector Detector, FaceLandmarker Landmarker) Graphs()
    {
        // Built on the first face and kept: neither graph is worth loading for
        // a photograph with no known face on it. Both files are checked before
        // either is opened, so a wrong file is reported as what it is rather
        // than as whatever the runtime makes of it.
        lock (_gate)
        {
            if (_detector is null || _landmarker is null)
            {
                string landmarkPath = _landmarkModelPath ?? _models.ResolvePath(ModelId.FaceLandmarks);
                RequireDetector();
                RequireLandmarks(landmarkPath);

                _landmarker ??= new FaceLandmarker(
                    new InferenceSession(landmarkPath, InferenceDevice.ForGraph()));
                _detector ??= new ScrfdFaceDetector(
                    new InferenceSession(_models.ResolvePath(ModelId.FaceDetection), InferenceDevice.ForGraph()));
            }

            return (_detector, _landmarker);
        }
    }

    private void RequireDetector()
    {
        ModelState state = _models.StateOf(ModelId.FaceDetection);
        if (state != ModelState.Ready)
        {
            throw new InvalidOperationException(
                $"The {ModelId.FaceDetection} model is {state}. It has to be installed before eyes "
                + "can be measured.");
        }
    }

    private static void RequireLandmarks(string landmarkPath)
    {
        ModelDescriptor landmarks = ModelManifest.Default.For(ModelId.FaceLandmarks);
        if (!ModelFile.Matches(landmarkPath, landmarks.Bytes, landmarks.Sha256))
        {
            throw new InvalidOperationException(
                $"{landmarkPath} is missing or is not InsightFace's {landmarks.FileName} "
                + $"({landmarks.Bytes:N0} bytes, SHA-256 {landmarks.Sha256}).");
        }
    }

    /// <summary>Waits for the measurements already running, then frees the graphs.</summary>
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
            _landmarker?.Dispose();
        }
        finally
        {
            _inUse.ExitWriteLock();
        }
    }
}
