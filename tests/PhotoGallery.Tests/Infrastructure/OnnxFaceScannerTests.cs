using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The tests that need the real weights are skipped unless
/// <c>PHOTOGALLERY_FACE_MODELS</c> names the folder holding them, since 182 MB
/// cannot live in the repository and its location is not this project's to
/// assume. The two that need nothing installed always run.
/// </summary>
public sealed class OnnxFaceScannerTests : IDisposable
{
    private const string ModelFolderVariable = "PHOTOGALLERY_FACE_MODELS";

    private readonly string _root;

    public OnnxFaceScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-faces-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Scan_OfSomethingThatIsNotAPictureReportsItUnread()
    {
        // Unread and "no faces in it" are different answers, and the pass treats
        // them differently: one is finished with, the other has not been looked
        // at. Nothing is loaded to establish this, so it costs nothing.
        string junk = Path.Combine(_root, "not-a-picture.jpg");
        await File.WriteAllTextAsync(junk, "this is not a JPEG");

        using var scanner = new OnnxFaceScanner(new StubModelStore(ModelState.Ready, _root));

        Assert.Null(await scanner.ScanAsync(junk));
    }

    [Fact]
    public async Task Scan_WithoutTheModelsInstalledSaysWhichOneIsMissing()
    {
        string picture = WriteFlatColour("plain.jpg", 320, 240);
        using var scanner = new OnnxFaceScanner(new StubModelStore(ModelState.Missing, _root));

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ScanAsync(picture));

        Assert.Contains("FaceDetection", error.Message, StringComparison.Ordinal);
        Assert.Contains("Missing", error.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Scan_FindsNoFacesInAFlatColour()
    {
        using OnnxFaceScanner scanner = RealScanner();
        string picture = WriteFlatColour("flat.jpg", 1024, 576);

        IReadOnlyList<ScannedFace>? faces = await scanner.ScanAsync(picture);

        Assert.NotNull(faces);
        Assert.Empty(faces);
    }

    [SkippableFact]
    public void Embed_ProducesAUnitLengthVectorOfTheExpectedWidth()
    {
        // Unit length is what makes the domain's similarity a dot product, and
        // it is also the cheapest possible check that the tensor was laid out
        // the way the graph expects.
        using ArcFaceEmbedder embedder = RealEmbedder();

        FaceEmbedding embedding = embedder.Embed(Noise());

        Assert.Equal(FaceEmbedding.Dimensions, embedding.Values.Length);
        Assert.Equal(1f, embedding.SimilarityTo(embedding), 4);
    }

    [SkippableFact]
    public void Embed_OfTheSameCropTwiceGivesTheSameAnswer()
    {
        using ArcFaceEmbedder embedder = RealEmbedder();
        BgrImage crop = Noise();

        Assert.Equal(1f, embedder.Embed(crop).SimilarityTo(embedder.Embed(crop)), 5);
    }

    [SkippableFact]
    public void Embed_RefusesACropOfTheWrongSize()
    {
        using ArcFaceEmbedder embedder = RealEmbedder();

        Assert.Throws<ArgumentException>(
            () => embedder.Embed(new BgrImage(new byte[64 * 64 * 3], 64, 64)));
    }

    private static string ModelFolder()
    {
        string? folder = Environment.GetEnvironmentVariable(ModelFolderVariable);
        Skip.If(
            string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder),
            $"Set {ModelFolderVariable} to the folder holding det_10g.onnx and w600k_r50.onnx.");

        return folder!;
    }

    private OnnxFaceScanner RealScanner() =>
        new(new StubModelStore(ModelState.Ready, ModelFolder()));

    private static ArcFaceEmbedder RealEmbedder() =>
        new(new Microsoft.ML.OnnxRuntime.InferenceSession(
            Path.Combine(ModelFolder(), "w600k_r50.onnx")));

    private static BgrImage Noise()
    {
        byte[] pixels = new byte[FaceAligner.CropSize * FaceAligner.CropSize * BgrImage.BytesPerPixel];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)((i * 37) % 256);
        }

        return new BgrImage(pixels, FaceAligner.CropSize, FaceAligner.CropSize);
    }

    private string WriteFlatColour(string name, int width, int height)
    {
        int stride = width * BgrImage.BytesPerPixel;
        byte[] pixels = new byte[stride * height];
        Array.Fill(pixels, (byte)128);

        BitmapSource source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        string path = Path.Combine(_root, name);
        using FileStream stream = File.Create(path);
        encoder.Save(stream);

        return path;
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
        private readonly string _folder;

        public StubModelStore(ModelState state, string folder)
        {
            _state = state;
            _folder = folder;
        }

        public ModelDescriptor Describe(ModelId id) =>
            new(id, 1, FileNameFor(id), 0, string.Empty, "test");

        public string ResolvePath(ModelId id) => Path.Combine(_folder, FileNameFor(id));

        public ModelState StateOf(ModelId id) => _state;

        public Task<ModelState> ImportAsync(
            ModelId id, string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_state);

        private static string FileNameFor(ModelId id) =>
            id == ModelId.FaceDetection ? "det_10g.onnx" : "w600k_r50.onnx";
    }
}
