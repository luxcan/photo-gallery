using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// Turns an aligned 112x112 crop into the 512 numbers that identify a face.
/// </summary>
/// <remarks>
/// Centred on 127.5 and divided by 127.5, which is not the same scaling the
/// detector uses - that one divides by 128. The two graphs were trained
/// separately and each expects its own, so the near-identical constants are
/// deliberate rather than a copy that drifted.
/// </remarks>
public sealed class ArcFaceEmbedder : IDisposable
{
    private const float PixelMean = 127.5f;

    private const float PixelScale = 127.5f;

    private readonly InferenceSession _session;
    private readonly string _inputName;

    public ArcFaceEmbedder(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
    }

    /// <summary>
    /// The embedding of one aligned crop, normalised to unit length so that
    /// comparing two of them is a dot product.
    /// </summary>
    public FaceEmbedding Embed(BgrImage crop)
    {
        ArgumentNullException.ThrowIfNull(crop);

        if (crop.Width != FaceAligner.CropSize || crop.Height != FaceAligner.CropSize)
        {
            throw new ArgumentException(
                $"The recognition model wants a {FaceAligner.CropSize}x{FaceAligner.CropSize} "
                + $"crop but was given {crop.Width}x{crop.Height}.",
                nameof(crop));
        }

        var input = new DenseTensor<float>([1, 3, FaceAligner.CropSize, FaceAligner.CropSize]);
        Span<float> values = input.Buffer.Span;
        int plane = FaceAligner.CropSize * FaceAligner.CropSize;

        for (int pixel = 0; pixel < plane; pixel++)
        {
            int source = pixel * BgrImage.BytesPerPixel;
            values[pixel] = (crop.Pixels[source + 2] - PixelMean) / PixelScale;
            values[plane + pixel] = (crop.Pixels[source + 1] - PixelMean) / PixelScale;
            values[(plane * 2) + pixel] = (crop.Pixels[source] - PixelMean) / PixelScale;
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
            _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);

        float[] embedding = outputs.First().AsTensor<float>().ToArray();
        if (embedding.Length != FaceEmbedding.Dimensions)
        {
            throw new InvalidOperationException(
                $"The recognition model produced {embedding.Length} numbers rather than "
                + $"{FaceEmbedding.Dimensions}.");
        }

        return new FaceEmbedding(Normalise(embedding));
    }

    /// <summary>
    /// Scales a vector to unit length.
    /// </summary>
    /// <remarks>
    /// Done here rather than left to whoever compares two of them, because the
    /// domain's similarity is a plain dot product and is only a cosine while
    /// this holds.
    /// </remarks>
    private static float[] Normalise(float[] values)
    {
        double sumOfSquares = 0d;
        foreach (float value in values)
        {
            sumOfSquares += (double)value * value;
        }

        double length = Math.Sqrt(sumOfSquares);
        if (length <= 0d || !double.IsFinite(length))
        {
            throw new InvalidOperationException(
                "The recognition model produced a vector with no length, which cannot be compared.");
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (float)(values[i] / length);
        }

        return values;
    }

    public void Dispose() => _session.Dispose();
}
