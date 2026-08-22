using System.Numerics;

namespace PhotoGallery.Infrastructure.Faces;

/// <summary>
/// Pixels as three tightly packed bytes each, blue first.
/// </summary>
/// <remarks>
/// Blue-green-red rather than the more obvious order because the reference
/// implementation this port has to agree with reads its images through OpenCV,
/// which is BGR, and swaps to RGB only at the moment it builds a tensor. Keeping
/// the same convention means the one place a channel order matters is the same
/// place in both, instead of being spread across every step.
/// </remarks>
public sealed class BgrImage
{
    public const int BytesPerPixel = 3;

    public BgrImage(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int expected = width * height * BytesPerPixel;
        if (pixels.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} bytes for {width}x{height} but got {pixels.Length}.",
                nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
    }

    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// Bilinear resampling, matching the half-pixel-centre convention the
    /// reference implementation's resize uses.
    /// </summary>
    /// <remarks>
    /// Written here rather than handed to the imaging stack because the
    /// landmarks this feeds decide how a face is aligned, and an alignment that
    /// is slightly off produces embeddings that look perfectly reasonable and
    /// match the wrong people. A resampler whose exact convention is visible is
    /// worth more than one that is merely convenient.
    ///
    /// <para>The convention is <c>src = (dst + 0.5) * scale - 0.5</c>. Dropping
    /// the half-pixel terms shifts everything by half a pixel at every scale
    /// change, which is small enough never to look wrong and large enough to
    /// move a landmark.</para>
    /// </remarks>
    public BgrImage Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        byte[] destination = new byte[width * height * BytesPerPixel];
        double scaleX = (double)Width / width;
        double scaleY = (double)Height / height;

        for (int y = 0; y < height; y++)
        {
            double sourceY = ((y + 0.5) * scaleY) - 0.5;
            int y0 = (int)Math.Floor(sourceY);
            double weightY = sourceY - y0;
            int y0Clamped = Math.Clamp(y0, 0, Height - 1);
            int y1Clamped = Math.Clamp(y0 + 1, 0, Height - 1);

            for (int x = 0; x < width; x++)
            {
                double sourceX = ((x + 0.5) * scaleX) - 0.5;
                int x0 = (int)Math.Floor(sourceX);
                double weightX = sourceX - x0;
                int x0Clamped = Math.Clamp(x0, 0, Width - 1);
                int x1Clamped = Math.Clamp(x0 + 1, 0, Width - 1);

                int topLeft = ((y0Clamped * Width) + x0Clamped) * BytesPerPixel;
                int topRight = ((y0Clamped * Width) + x1Clamped) * BytesPerPixel;
                int bottomLeft = ((y1Clamped * Width) + x0Clamped) * BytesPerPixel;
                int bottomRight = ((y1Clamped * Width) + x1Clamped) * BytesPerPixel;
                int target = ((y * width) + x) * BytesPerPixel;

                for (int channel = 0; channel < BytesPerPixel; channel++)
                {
                    double top = (Pixels[topLeft + channel] * (1 - weightX))
                               + (Pixels[topRight + channel] * weightX);
                    double bottom = (Pixels[bottomLeft + channel] * (1 - weightX))
                                  + (Pixels[bottomRight + channel] * weightX);

                    destination[target + channel] =
                        (byte)Math.Clamp(Math.Round((top * (1 - weightY)) + (bottom * weightY)), 0, 255);
                }
            }
        }

        return new BgrImage(destination, width, height);
    }

    /// <summary>
    /// Bicubic resampling with the filter widened as the picture shrinks, which
    /// is the convention Pillow uses and therefore the one CLIP was trained on.
    /// </summary>
    /// <remarks>
    /// A second resampler rather than an option on <see cref="Resize"/>, because
    /// the two have to disagree. That one reproduces OpenCV's bilinear exactly,
    /// down to sampling only the two nearest pixels however far the picture is
    /// being shrunk, because the face detector's landmarks are compared against a
    /// reference implementation that does the same. This one reproduces Pillow's
    /// bicubic, which widens its window by the reduction factor so that shrinking
    /// a 1024px preview to 224 averages the pixels in between instead of point
    /// sampling every fifth one. Making either behave like the other would break
    /// the feature that depends on it.
    ///
    /// <para>Separable: the horizontal pass runs once per source row and the
    /// vertical pass over its result, which is what keeps a 4x4 kernel from
    /// costing sixteen samples a pixel.</para>
    /// </remarks>
    public BgrImage ResizeBicubic(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        float[] horizontal = Resample(Pixels, Width, Height, width, horizontally: true);
        float[] vertical = Resample(horizontal, width, Height, height, horizontally: false);

        byte[] destination = new byte[width * height * BytesPerPixel];
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (byte)Math.Clamp(MathF.Round(vertical[i]), 0, 255);
        }

        return new BgrImage(destination, width, height);
    }

    /// <summary>
    /// One axis of the resample, reading bytes or floats and always writing
    /// floats so the two passes compose without rounding in between.
    /// </summary>
    private static float[] Resample<T>(
        T[] source, int sourceWidth, int sourceHeight, int target, bool horizontally)
        where T : INumberBase<T>
    {
        int outWidth = horizontally ? target : sourceWidth;
        int outHeight = horizontally ? sourceHeight : target;
        int from = horizontally ? sourceWidth : sourceHeight;

        var result = new float[outWidth * outHeight * BytesPerPixel];

        double scale = (double)from / target;
        double support = CubicSupport * Math.Max(1d, scale);
        double step = Math.Max(1d, scale);

        // Sized from the scale rather than fixed, and allocated once for the
        // axis. A fixed buffer would not overflow, it would quietly drop the far
        // side of the kernel on a large reduction and shift the picture by half
        // a window - the sort of wrong that looks like a slightly soft image.
        double[] weights = new double[(int)Math.Ceiling(2d * support) + 2];

        for (int to = 0; to < target; to++)
        {
            double centre = ((to + 0.5) * scale) - 0.5;
            int first = (int)Math.Ceiling(centre - support);
            int last = (int)Math.Floor(centre + support);

            int count = Math.Min(last - first + 1, weights.Length);
            double total = 0d;

            for (int i = 0; i < count; i++)
            {
                double weight = Cubic((first + i - centre) / step);
                weights[i] = weight;
                total += weight;
            }

            if (total == 0d)
            {
                continue;
            }

            // The other axis, whichever it is, is walked whole.
            int lines = horizontally ? sourceHeight : sourceWidth;
            for (int line = 0; line < lines; line++)
            {
                for (int channel = 0; channel < BytesPerPixel; channel++)
                {
                    double sum = 0d;
                    for (int i = 0; i < count; i++)
                    {
                        int at = Math.Clamp(first + i, 0, from - 1);
                        int index = horizontally
                            ? (((line * sourceWidth) + at) * BytesPerPixel) + channel
                            : (((at * sourceWidth) + line) * BytesPerPixel) + channel;

                        sum += double.CreateChecked(source[index]) * weights[i];
                    }

                    int destination = horizontally
                        ? (((line * outWidth) + to) * BytesPerPixel) + channel
                        : (((to * outWidth) + line) * BytesPerPixel) + channel;

                    result[destination] = (float)(sum / total);
                }
            }
        }

        return result;
    }

    /// <summary>How far the cubic kernel reaches, in source pixels.</summary>
    private const double CubicSupport = 2d;

    /// <summary>
    /// The Catmull-Rom cubic, <c>a = -0.5</c>.
    /// </summary>
    /// <remarks>
    /// Pillow's coefficient, not OpenCV's -0.75. The preprocessing this feeds was
    /// described by open_clip, which resizes through Pillow, and the two produce
    /// visibly different edges.
    /// </remarks>
    private static double Cubic(double x)
    {
        const double A = -0.5d;
        x = Math.Abs(x);

        if (x < 1d)
        {
            return (((A + 2d) * x - (A + 3d)) * x * x) + 1d;
        }

        return x < 2d
            ? ((((x - 5d) * x) + 8d) * x - 4d) * A
            : 0d;
    }

    /// <summary>
    /// The middle <paramref name="width"/> by <paramref name="height"/> of the
    /// picture.
    /// </summary>
    /// <remarks>
    /// The second half of CLIP's preprocessing: the shortest edge is resized to
    /// 224 and then the middle square is taken, so a landscape photograph is
    /// judged on its centre rather than squashed.
    /// </remarks>
    public BgrImage CropCentre(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (width > Width || height > Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"Cannot cut {width}x{height} out of {Width}x{Height}.");
        }

        int left = (Width - width) / 2;
        int top = (Height - height) / 2;

        byte[] destination = new byte[width * height * BytesPerPixel];
        for (int y = 0; y < height; y++)
        {
            int source = (((top + y) * Width) + left) * BytesPerPixel;
            Pixels.AsSpan(source, width * BytesPerPixel)
                .CopyTo(destination.AsSpan(y * width * BytesPerPixel));
        }

        return new BgrImage(destination, width, height);
    }

    /// <summary>The colour at a pixel, or black outside the image.</summary>
    public void Sample(int x, int y, Span<byte> destination)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            destination.Clear();
            return;
        }

        Pixels.AsSpan(((y * Width) + x) * BytesPerPixel, BytesPerPixel).CopyTo(destination);
    }
}
