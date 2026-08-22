using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// Finds faces with the SCRFD detection graph.
/// </summary>
/// <remarks>
/// The runtime will happily execute the model; what it will not do is the
/// arithmetic around it. The graph emits, per feature level, a score and four
/// distances from an implied grid point rather than boxes, so decoding that grid
/// and then suppressing the overlaps is this class's whole job - and it has to
/// agree with the reference implementation exactly, because the landmarks it
/// produces decide how each face is aligned.
/// </remarks>
public sealed class ScrfdFaceDetector : IDisposable
{
    /// <summary>
    /// The square the picture is fitted into before detection.
    /// </summary>
    /// <remarks>
    /// 640 rather than 480. The smaller input measured 657 ms against 676 ms -
    /// no useful saving - and lost 3 of 42 faces on the sample it was tried on.
    /// </remarks>
    public const int InputEdge = 640;

    /// <summary>
    /// Below this the detector is usually not looking at a face at all.
    /// </summary>
    public const float ScoreThreshold = 0.5f;

    /// <summary>How much two boxes may overlap before the weaker one is dropped.</summary>
    private const float OverlapThreshold = 0.4f;

    /// <summary>Grid points per cell, per feature level.</summary>
    private const int AnchorsPerCell = 2;

    private const float PixelMean = 127.5f;

    private const float PixelScale = 128f;

    private static readonly int[] s_strides = [8, 16, 32];

    private readonly InferenceSession _session;
    private readonly string _inputName;

    public ScrfdFaceDetector(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
    }

    public IReadOnlyList<DetectedFace> Detect(BgrImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Fitted into the square by its longer edge and pinned to the top left,
        // exactly as the reference does - including the truncation, because the
        // scale everything is divided back by is derived from the rounded size
        // rather than from the ratio.
        double imageRatio = (double)image.Height / image.Width;
        int fittedWidth, fittedHeight;
        if (imageRatio > 1d)
        {
            fittedHeight = InputEdge;
            fittedWidth = (int)(fittedHeight / imageRatio);
        }
        else
        {
            fittedWidth = InputEdge;
            fittedHeight = (int)(fittedWidth * imageRatio);
        }

        fittedWidth = Math.Max(1, fittedWidth);
        fittedHeight = Math.Max(1, fittedHeight);
        float detectionScale = (float)fittedHeight / image.Height;

        BgrImage fitted = image.Resize(fittedWidth, fittedHeight);
        DenseTensor<float> input = BuildInput(fitted);

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
            _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, input)]);

        List<DetectedFace> candidates = Decode(outputs, detectionScale);
        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));

        return Suppress(candidates);
    }

    /// <summary>
    /// The square tensor the graph expects: red first, centred and scaled.
    /// </summary>
    /// <remarks>
    /// The padding is not zero. The reference pads the picture with black pixels
    /// and only then centres, so the empty region arrives as -0.996 rather than
    /// 0 - a difference the model notices at the edges of a portrait photo.
    /// </remarks>
    private static DenseTensor<float> BuildInput(BgrImage fitted)
    {
        var input = new DenseTensor<float>([1, 3, InputEdge, InputEdge]);
        const float Padding = (0f - PixelMean) / PixelScale;

        Span<float> values = input.Buffer.Span;
        values.Fill(Padding);

        int plane = InputEdge * InputEdge;
        for (int y = 0; y < fitted.Height; y++)
        {
            for (int x = 0; x < fitted.Width; x++)
            {
                int source = ((y * fitted.Width) + x) * BgrImage.BytesPerPixel;
                int target = (y * InputEdge) + x;

                values[target] = (fitted.Pixels[source + 2] - PixelMean) / PixelScale;
                values[plane + target] = (fitted.Pixels[source + 1] - PixelMean) / PixelScale;
                values[(plane * 2) + target] = (fitted.Pixels[source] - PixelMean) / PixelScale;
            }
        }

        return input;
    }

    /// <summary>
    /// Turns the graph's per-grid-point distances back into boxes and landmarks.
    /// </summary>
    /// <remarks>
    /// Outputs are matched by their width rather than by position in the list -
    /// one column is a score, four are a box, ten are landmarks - and then
    /// ordered by how many rows each has, since a finer feature level always has
    /// more. Relying on the declared order instead would make a differently
    /// exported model decode into nonsense without failing.
    /// </remarks>
    private static List<DetectedFace> Decode(
        IReadOnlyCollection<DisposableNamedOnnxValue> outputs, float detectionScale)
    {
        List<float[]> scores = [], boxes = [], landmarks = [];

        foreach (DisposableNamedOnnxValue output in outputs)
        {
            Tensor<float> tensor = output.AsTensor<float>();
            float[] values = tensor.ToArray();
            int columns = tensor.Dimensions[^1];

            switch (columns)
            {
                case 1: scores.Add(values); break;
                case 4: boxes.Add(values); break;
                case 10: landmarks.Add(values); break;
                default:
                    throw new InvalidOperationException(
                        $"The detection model produced an output {columns} wide, which is none "
                        + "of a score, a box or a set of landmarks.");
            }
        }

        if (scores.Count != s_strides.Length
            || boxes.Count != s_strides.Length
            || landmarks.Count != s_strides.Length)
        {
            throw new InvalidOperationException(
                $"Expected {s_strides.Length} outputs of each kind but got {scores.Count} scores, "
                + $"{boxes.Count} boxes and {landmarks.Count} landmark sets.");
        }

        scores.Sort((left, right) => right.Length.CompareTo(left.Length));
        boxes.Sort((left, right) => (right.Length / 4).CompareTo(left.Length / 4));
        landmarks.Sort((left, right) => (right.Length / 10).CompareTo(left.Length / 10));

        var found = new List<DetectedFace>();

        for (int level = 0; level < s_strides.Length; level++)
        {
            int stride = s_strides[level];
            int cells = InputEdge / stride;
            int expected = cells * cells * AnchorsPerCell;

            if (scores[level].Length != expected)
            {
                throw new InvalidOperationException(
                    $"Stride {stride} should carry {expected} grid points but carries "
                    + $"{scores[level].Length}.");
            }

            float[] levelScores = scores[level];
            float[] levelBoxes = boxes[level];
            float[] levelLandmarks = landmarks[level];

            for (int y = 0; y < cells; y++)
            {
                for (int x = 0; x < cells; x++)
                {
                    for (int anchor = 0; anchor < AnchorsPerCell; anchor++)
                    {
                        int index = ((((y * cells) + x) * AnchorsPerCell) + anchor);
                        float score = levelScores[index];
                        if (score < ScoreThreshold)
                        {
                            continue;
                        }

                        float centreX = x * stride;
                        float centreY = y * stride;

                        int box = index * 4;
                        float left = centreX - (levelBoxes[box] * stride);
                        float top = centreY - (levelBoxes[box + 1] * stride);
                        float right = centreX + (levelBoxes[box + 2] * stride);
                        float bottom = centreY + (levelBoxes[box + 3] * stride);

                        float[] points = new float[FaceAligner.LandmarkCount * 2];
                        int landmark = index * points.Length;
                        for (int point = 0; point < FaceAligner.LandmarkCount; point++)
                        {
                            points[point * 2] =
                                (centreX + (levelLandmarks[landmark + (point * 2)] * stride))
                                / detectionScale;
                            points[(point * 2) + 1] =
                                (centreY + (levelLandmarks[landmark + (point * 2) + 1] * stride))
                                / detectionScale;
                        }

                        // Scaled back to the original picture before suppression,
                        // as the reference does: the overlap measure carries a
                        // one-pixel term, so the order of the two steps shows.
                        found.Add(new DetectedFace(
                            left / detectionScale,
                            top / detectionScale,
                            right / detectionScale,
                            bottom / detectionScale,
                            score,
                            points));
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Keeps the strongest of each cluster of overlapping boxes.
    /// </summary>
    private static List<DetectedFace> Suppress(List<DetectedFace> candidates)
    {
        var kept = new List<DetectedFace>();
        bool[] dropped = new bool[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            if (dropped[i])
            {
                continue;
            }

            DetectedFace winner = candidates[i];
            kept.Add(winner);

            for (int j = i + 1; j < candidates.Count; j++)
            {
                if (!dropped[j] && Overlap(winner, candidates[j]) > OverlapThreshold)
                {
                    dropped[j] = true;
                }
            }
        }

        return kept;
    }

    private static float Overlap(DetectedFace left, DetectedFace right)
    {
        float width = MathF.Max(0f, MathF.Min(left.Right, right.Right)
                                  - MathF.Max(left.Left, right.Left) + 1f);
        float height = MathF.Max(0f, MathF.Min(left.Bottom, right.Bottom)
                                   - MathF.Max(left.Top, right.Top) + 1f);

        float intersection = width * height;
        float union = ((left.Width + 1f) * (left.Height + 1f))
                    + ((right.Width + 1f) * (right.Height + 1f))
                    - intersection;

        return union <= 0f ? 0f : intersection / union;
    }

    public void Dispose() => _session.Dispose();
}
