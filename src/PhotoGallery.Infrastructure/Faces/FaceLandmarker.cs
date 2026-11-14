using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// Places InsightFace's 106 landmarks on one face, with the 2d106det graph.
/// </summary>
/// <remarks>
/// Every number here was confirmed against the graph and against real faces
/// from the library, not taken from memory (PRP 14, phase 1):
///
/// <list type="bullet">
/// <item><b>Crop.</b> A 192x192 square centred on the face box, scaled so the
/// box's longer side times 1.5 fills it, with no rotation - the reference's
/// <c>Landmark.get</c>. The margin matters: the graph was trained on a loose
/// crop, and a tight one puts the brows and chin at the edge.</item>
/// <item><b>Pixels.</b> Raw 0-255, red first. The graph's own first two nodes
/// are <c>Sub 127.5</c> and <c>Mul 0.0078125</c> (read from the file's
/// initialisers), so centring and scaling here as well would apply them twice.
/// Red first because the reference builds its blob with swapRB.</item>
/// <item><b>Output.</b> 212 numbers, x and y alternating, each in -1..1 across
/// the crop: <c>(p + 1) * 96</c> is the crop pixel, mapped back through the
/// inverse of the crop transform.</item>
/// </list>
/// </remarks>
public sealed class FaceLandmarker : IDisposable
{
    /// <summary>The square the face is cut into before the graph sees it.</summary>
    public const int InputEdge = 192;

    /// <summary>How many points the graph places on a face.</summary>
    public const int PointCount = 106;

    /// <summary>How much larger than the detector's box the crop is.</summary>
    public const float CropMargin = 1.5f;

    private readonly InferenceSession _session;
    private readonly string _inputName;

    public FaceLandmarker(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
    }

    /// <summary>
    /// The 106 points of the face inside <paramref name="box"/>, in the
    /// picture's own pixels, as x and y alternating - or null when the box is
    /// empty.
    /// </summary>
    /// <param name="image">The picture the box was found in.</param>
    /// <param name="box">The detector's box for the face.</param>
    /// <param name="roll">
    /// How far the line through the eyes is turned from level, in radians,
    /// clockwise as the picture is drawn; zero when it is not known.
    /// </param>
    public float[]? Locate(BgrImage image, FaceBounds box, float roll)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (box.Width <= 0 || box.Height <= 0 || !float.IsFinite(roll))
        {
            return null;
        }

        SimilarityTransform toCrop = CropFor(box, roll);
        BgrImage? crop = FaceAligner.Warp(image, toCrop, InputEdge, InputEdge);
        if (crop is null || !toCrop.TryInvert(out SimilarityTransform toImage))
        {
            return null;
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
            _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, BuildInput(crop))]);

        float[] raw = outputs.First().AsTensor<float>().ToArray();
        if (raw.Length != PointCount * 2)
        {
            throw new InvalidOperationException(
                $"The landmark model produced {raw.Length} numbers rather than {PointCount * 2}.");
        }

        const float Half = InputEdge / 2f;
        float[] points = new float[raw.Length];
        for (int i = 0; i < PointCount; i++)
        {
            (float x, float y) = toImage.Apply((raw[i * 2] + 1f) * Half, (raw[(i * 2) + 1] + 1f) * Half);
            points[i * 2] = x;
            points[(i * 2) + 1] = y;
        }

        return points;
    }

    /// <summary>
    /// The box's centre lands on the crop's centre, its longer side widened by
    /// <see cref="CropMargin"/> spans the crop, and the face is turned level.
    /// </summary>
    /// <remarks>
    /// Turning it level is the one departure from the reference, which crops
    /// upright whatever the face is doing. On this library that is wrong often
    /// enough to matter: a baby asleep on its side is a face turned a quarter of
    /// the way round, and given one the graph scattered the eye points down the
    /// cheek. The detector's two eye points already say which way is level, so
    /// the graph is shown the face the way it was trained on.
    /// </remarks>
    public static SimilarityTransform CropFor(FaceBounds box, float roll)
    {
        float centreX = box.X + (box.Width / 2f);
        float centreY = box.Y + (box.Height / 2f);
        float scale = InputEdge / (Math.Max(box.Width, box.Height) * CropMargin);

        // Turning by -roll brings the eye line back to level.
        float a = scale * MathF.Cos(-roll);
        float b = scale * MathF.Sin(-roll);

        return new SimilarityTransform(
            a,
            b,
            (InputEdge / 2f) - ((a * centreX) - (b * centreY)),
            (InputEdge / 2f) - ((b * centreX) + (a * centreY)));
    }

    private static DenseTensor<float> BuildInput(BgrImage crop)
    {
        var input = new DenseTensor<float>([1, 3, InputEdge, InputEdge]);
        Span<float> values = input.Buffer.Span;
        int plane = InputEdge * InputEdge;

        for (int pixel = 0; pixel < plane; pixel++)
        {
            int source = pixel * BgrImage.BytesPerPixel;
            values[pixel] = crop.Pixels[source + 2];
            values[plane + pixel] = crop.Pixels[source + 1];
            values[(plane * 2) + pixel] = crop.Pixels[source];
        }

        return input;
    }

    public void Dispose() => _session.Dispose();
}
