using Microsoft.ML.OnnxRuntime.Tensors;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Infrastructure.Search;

/// <summary>
/// Turns a decoded picture into the tensor the visual encoder was trained on.
/// </summary>
/// <remarks>
/// Every number here comes from the model's own <c>preprocess_cfg.json</c>
/// rather than from memory: 224 square, RGB, bicubic, shortest edge resized and
/// then the middle taken. Preprocessing that disagrees with training does not
/// throw - it shifts every vector slightly and quietly makes the search worse,
/// which is the same class of mistake as a misaligned face crop and just as
/// invisible.
/// </remarks>
internal static class ClipPreprocessing
{
    public const int InputEdge = 224;

    /// <summary>Channel means, in the model's RGB order.</summary>
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];

    private static readonly float[] Deviation = [0.26862954f, 0.26130258f, 0.27577711f];

    /// <summary>
    /// The picture as one 1x3x224x224 batch.
    /// </summary>
    /// <remarks>
    /// Shortest edge to 224 and then a centre crop, so a landscape photograph is
    /// judged on its middle rather than squashed into a square. The alternative,
    /// fitting the whole frame and padding, was not what the model saw.
    /// </remarks>
    public static DenseTensor<float> ToTensor(BgrImage picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        double scale = (double)InputEdge / Math.Min(picture.Width, picture.Height);
        int width = Math.Max(InputEdge, (int)Math.Round(picture.Width * scale));
        int height = Math.Max(InputEdge, (int)Math.Round(picture.Height * scale));

        BgrImage square = picture
            .ResizeBicubic(width, height)
            .CropCentre(InputEdge, InputEdge);

        var input = new DenseTensor<float>([1, 3, InputEdge, InputEdge]);
        Span<float> values = input.Buffer.Span;
        int plane = InputEdge * InputEdge;

        for (int pixel = 0; pixel < plane; pixel++)
        {
            int source = pixel * BgrImage.BytesPerPixel;

            // Red first: the image is held blue-green-red, as OpenCV would, and
            // the swap to the model's order happens here and nowhere else.
            values[pixel] = Scale(square.Pixels[source + 2], 0);
            values[plane + pixel] = Scale(square.Pixels[source + 1], 1);
            values[(plane * 2) + pixel] = Scale(square.Pixels[source], 2);
        }

        return input;
    }

    private static float Scale(byte value, int channel) =>
        ((value / 255f) - Mean[channel]) / Deviation[channel];
}
