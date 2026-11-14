using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The eye measurer around its graphs. The tests that need the real weights are
/// skipped unless <c>PHOTOGALLERY_FACE_MODELS</c> names a folder holding
/// det_10g.onnx and 2d106det.onnx, as for the face scanner; the rest need
/// nothing installed.
/// </summary>
public sealed class OnnxEyeMeasurerTests : IDisposable
{
    private const string ModelFolderVariable = "PHOTOGALLERY_FACE_MODELS";

    private readonly string _root;

    public OnnxEyeMeasurerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-eyes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Measure_OfSomethingThatIsNotAPictureReportsItUnread()
    {
        string junk = Path.Combine(_root, "not-a-picture.jpg");
        await File.WriteAllTextAsync(junk, "this is not a JPEG");

        using var measurer = new OnnxEyeMeasurer(
            new StubModelStore(ModelState.Ready, _root), Path.Combine(_root, "2d106det.onnx"));

        Assert.Null(await measurer.MeasureAsync(junk, [new FaceBounds(0, 0, 10, 10)]));
    }

    [Fact]
    public void Measure_OfNoFacesLoadsNothingAndReturnsNothing()
    {
        // The graphs are only built when there is a face to measure, so a
        // missing model costs nothing on a photograph with no known faces.
        using var measurer = new OnnxEyeMeasurer(
            new StubModelStore(ModelState.Missing, _root), Path.Combine(_root, "missing.onnx"));

        Assert.Empty(measurer.Measure(Flat(64, 48), []));
    }

    [Fact]
    public void Measure_WithoutTheDetectorInstalledSaysSo()
    {
        using var measurer = new OnnxEyeMeasurer(
            new StubModelStore(ModelState.Missing, _root), Path.Combine(_root, "2d106det.onnx"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => measurer.Measure(Flat(64, 48), [new FaceBounds(0, 0, 10, 10)]));

        Assert.Contains("FaceDetection", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Measure_RefusesALandmarkFileThatIsNotTheModel()
    {
        // Proved by length and digest before it is opened, as every model is: a
        // wrong file does not fail, it places 106 confident points somewhere.
        string impostor = Path.Combine(_root, "2d106det.onnx");
        File.WriteAllBytes(impostor, new byte[128]);
        string detector = Path.Combine(_root, "det_10g.onnx");
        File.WriteAllBytes(detector, new byte[128]);

        using var measurer = new OnnxEyeMeasurer(new StubModelStore(ModelState.Ready, _root), impostor);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => measurer.Measure(Flat(64, 48), [new FaceBounds(0, 0, 10, 10)]));

        Assert.Contains("2d106det.onnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Measure_AfterDisposalRefuses()
    {
        var measurer = new OnnxEyeMeasurer(
            new StubModelStore(ModelState.Ready, _root), Path.Combine(_root, "2d106det.onnx"));
        measurer.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => measurer.Measure(Flat(64, 48), [new FaceBounds(0, 0, 10, 10)]));
    }

    [Fact]
    public void Measure_ReadsTheLandmarkFileFromTheModelsFolderInUseNow()
    {
        // The models folder can be moved while the app runs. The landmark file
        // has to be looked for where the store says now, as the detector is,
        // not where it was the first time the measurer was built.
        string before = Path.Combine(_root, "before");
        string after = Path.Combine(_root, "after");
        Directory.CreateDirectory(before);
        Directory.CreateDirectory(after);
        var models = new StubModelStore(ModelState.Ready, before);
        using var measurer = new OnnxEyeMeasurer(models);

        models.Folder = after;
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => measurer.Measure(Flat(64, 48), [new FaceBounds(0, 0, 10, 10)]));

        Assert.Contains(Path.Combine(after, "2d106det.onnx"), error.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task MeasureAsync_WhenTheGraphsCannotRunSaysNothingRatherThanUnknown()
    {
        // A graph that will not load or run is not a face that could not be
        // told: answering Unknown would be kept as a real reading and the
        // photograph never measured again. Nothing is the answer that leaves it
        // due. A detector file the runtime cannot parse stands in for a card
        // that fails; the landmark file is the real one, so the digest passes.
        string folder = ModelFolder();
        File.Copy(Path.Combine(folder, "2d106det.onnx"), Path.Combine(_root, "2d106det.onnx"));
        File.WriteAllBytes(Path.Combine(_root, "det_10g.onnx"), new byte[128]);
        string preview = Path.Combine(_root, "preview.jpg");
        await File.WriteAllBytesAsync(preview, TestJpeg.Bytes(64, 48));

        using var measurer = new OnnxEyeMeasurer(
            new StubModelStore(ModelState.Ready, _root), Path.Combine(_root, "2d106det.onnx"));

        Assert.Null(await measurer.MeasureAsync(preview, [new FaceBounds(0, 0, 10, 10)]));
    }

    [SkippableFact]
    public void Measure_OfABoxTheDetectorDoesNotFindIsUnmeasured()
    {
        // No face where the box says: the five points that decide the pose are
        // missing, so nothing is claimed about the eyes.
        string folder = ModelFolder();
        using var measurer = new OnnxEyeMeasurer(
            new StubModelStore(ModelState.Ready, folder), Path.Combine(folder, "2d106det.onnx"));

        IReadOnlyList<MeasuredEyes> measured =
            measurer.Measure(Flat(640, 480), [new FaceBounds(200, 150, 120, 140)]);

        MeasuredEyes only = Assert.Single(measured);
        Assert.Equal(EyeMeasurement.Unmeasured, only.Measurement);
        Assert.Null(only.Landmarks);
    }

    [SkippableFact]
    public void Locate_PlacesEveryPointNearTheBox()
    {
        // Whatever it sees, the graph's points are in -1..1 of its crop, so once
        // mapped back they must sit within the crop around the box - a wrong
        // scale or a missed inverse would throw them across the picture.
        string folder = ModelFolder();
        using var landmarker = new FaceLandmarker(
            new Microsoft.ML.OnnxRuntime.InferenceSession(Path.Combine(folder, "2d106det.onnx")));
        FaceBounds box = new(200, 150, 120, 140);

        float[] points = landmarker.Locate(Flat(640, 480), box, roll: 0f)!;

        Assert.Equal(FaceLandmarker.PointCount * 2, points.Length);
        float reach = Math.Max(box.Width, box.Height) * FaceLandmarker.CropMargin / 2f;
        for (int i = 0; i < FaceLandmarker.PointCount; i++)
        {
            Assert.InRange(points[i * 2], 260f - reach, 260f + reach);
            Assert.InRange(points[(i * 2) + 1], 220f - reach, 220f + reach);
        }
    }

    private static string ModelFolder()
    {
        string? folder = Environment.GetEnvironmentVariable(ModelFolderVariable);
        Skip.If(
            string.IsNullOrWhiteSpace(folder)
            || !File.Exists(Path.Combine(folder, "det_10g.onnx"))
            || !File.Exists(Path.Combine(folder, "2d106det.onnx")),
            $"Set {ModelFolderVariable} to the folder holding det_10g.onnx and 2d106det.onnx.");

        return folder!;
    }

    private static BgrImage Flat(int width, int height)
    {
        byte[] pixels = new byte[width * height * BgrImage.BytesPerPixel];
        Array.Fill(pixels, (byte)128);
        return new BgrImage(pixels, width, height);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test.
        }
    }

    private sealed class StubModelStore : IModelStore
    {
        private readonly ModelState _state;

        public StubModelStore(ModelState state, string folder)
        {
            _state = state;
            Folder = folder;
        }

        /// <summary>Where the store says the models are; moved to follow a change of folder.</summary>
        public string Folder { get; set; }

        public ModelDescriptor Describe(ModelId id) =>
            new(id, 1, "det_10g.onnx", 0, string.Empty, "test");

        public string ResolvePath(ModelId id) =>
            Path.Combine(Folder, id == ModelId.FaceLandmarks ? "2d106det.onnx" : "det_10g.onnx");

        public ModelState StateOf(ModelId id) => _state;

        public Task<ModelState> ImportAsync(
            ModelId id, string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_state);
    }
}
