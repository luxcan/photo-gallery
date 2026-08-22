using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Search;
using PhotoGallery.Domain.Vectors;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Infrastructure.Search;

/// <inheritdoc cref="IContentEncoder"/>
/// <remarks>
/// Shaped like <see cref="OnnxFaceScanner"/>, and for the same reasons: the
/// graphs are built once and kept, each is held to a single thread because the
/// pass owns the parallelism, and a scan holds a shared lock for the whole of
/// its inference so that disposing the encoder mid-pass waits rather than
/// freeing a session out from under a thread reading it.
/// </remarks>
public sealed class ClipContentEncoder : IContentEncoder, IDisposable
{
    private readonly IModelStore _models;
    private readonly Lock _gate = new();
    private readonly ReaderWriterLockSlim _inUse = new();

    private InferenceSession? _vision;
    private InferenceSession? _text;
    private ClipTokenizer? _tokenizer;
    private bool _disposed;

    public ClipContentEncoder(IModelStore models) => _models = models;

    /// <summary>Whether every file this needs is present and verified.</summary>
    public bool IsReady =>
        _models.StateOf(ModelId.ContentVision) == ModelState.Ready
        && _models.StateOf(ModelId.ContentText) == ModelState.Ready
        && _models.StateOf(ModelId.ContentVocabulary) == ModelState.Ready
        && _models.StateOf(ModelId.ContentMerges) == ModelState.Ready;

    public Task<ContentEmbedding?> DescribePictureAsync(
        string previewPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewPath);

        // Wholly synchronous, processor-bound work, and the heaviest in the app:
        // left on the calling thread it would hold whatever asked for it.
        return Task.Run<ContentEmbedding?>(
            () => DescribePicture(previewPath, cancellationToken), cancellationToken);
    }

    public Task<ContentEmbedding?> DescribePhraseAsync(
        string phrase, CancellationToken cancellationToken = default) =>
        Task.Run<ContentEmbedding?>(() => DescribePhrase(phrase), cancellationToken);

    private ContentEmbedding? DescribePicture(string previewPath, CancellationToken cancellationToken)
    {
        BgrImage? preview = Decode(previewPath);
        if (preview is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        DenseTensor<float> input = ClipPreprocessing.ToTensor(preview);

        _inUse.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Run(VisionGraph(), "image", input);
        }
        finally
        {
            _inUse.ExitReadLock();
        }
    }

    private ContentEmbedding? DescribePhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase))
        {
            return null;
        }

        _inUse.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            (InferenceSession text, ClipTokenizer tokenizer) = TextGraph();

            int[] ids = tokenizer.Encode(phrase);
            var input = new DenseTensor<int>([1, ClipTokenizer.ContextLength]);
            for (int i = 0; i < ids.Length; i++)
            {
                input[0, i] = ids[i];
            }

            return Run(text, "text", input);
        }
        finally
        {
            _inUse.ExitReadLock();
        }
    }

    /// <summary>
    /// One pass through a graph, scaled to unit length.
    /// </summary>
    /// <remarks>
    /// Normalised here whatever the export does, because the domain's similarity
    /// is a plain dot product and is only a cosine while that holds. Scaling an
    /// already-unit vector changes nothing, so this costs a pass over 768 floats
    /// to remove an assumption about somebody else's export.
    /// </remarks>
    private static ContentEmbedding? Run<T>(InferenceSession session, string input, Tensor<T> values)
    {
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
            session.Run([NamedOnnxValue.CreateFromTensor(input, values)]);

        float[] embedding = outputs.First().AsTensor<float>().ToArray();
        if (embedding.Length != ContentEmbedding.Dimensions)
        {
            throw new InvalidOperationException(
                $"The {input} encoder produced {embedding.Length} numbers rather than "
                + $"{ContentEmbedding.Dimensions}.");
        }

        return UnitVectors.TryNormalise(embedding) ? new ContentEmbedding(embedding) : null;
    }

    /// <summary>
    /// The graph that reads pictures, built on first use.
    /// </summary>
    /// <remarks>
    /// Kept apart from the text side deliberately. The two are loaded by
    /// different things at different times: describing the library needs this
    /// one and never the other, and answering a search needs the other and never
    /// this one. Built together, a single typed query would pull 1.2 GB of
    /// visual weights off disk to encode three words.
    /// </remarks>
    private InferenceSession VisionGraph()
    {
        lock (_gate)
        {
            return _vision ??= Open(ModelId.ContentVision);
        }
    }

    /// <summary>The graph that reads phrases, and the vocabulary it needs.</summary>
    private (InferenceSession Text, ClipTokenizer Tokenizer) TextGraph()
    {
        lock (_gate)
        {
            _text ??= Open(ModelId.ContentText);
            _tokenizer ??= new ClipTokenizer(
                Resolve(ModelId.ContentVocabulary), Resolve(ModelId.ContentMerges));

            return (_text, _tokenizer);
        }
    }

    private InferenceSession Open(ModelId id) =>
        new(Resolve(id), new SessionOptions
        {
            // One thread per graph: the pass already runs several pictures at
            // once, and letting each session spread over every core as well
            // would leave the machine competing with itself.
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        });

    private string Resolve(ModelId id)
    {
        ModelState state = _models.StateOf(id);
        return state == ModelState.Ready
            ? _models.ResolvePath(id)
            : throw new InvalidOperationException(
                $"The {id} file is {state}. It has to be installed before pictures can be "
                + "searched by what is in them.");
    }

    /// <summary>
    /// The preview as plain blue-green-red bytes, or null when it will not open.
    /// </summary>
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
            // as a photograph of nothing.
            return null;
        }
    }

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
            _vision?.Dispose();
            _text?.Dispose();
        }
        finally
        {
            _inUse.ExitWriteLock();
        }
    }
}
