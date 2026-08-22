using System.Globalization;
using System.Numerics;

namespace PhotoGallery.Domain.Assets;

/// <summary>
/// A 64-bit perceptual hash of an image. Unlike a content hash, two values can
/// be <em>close</em> - which is how the same photo saved at a different size or
/// quality is recognised.
/// </summary>
public readonly record struct PerceptualHash(ulong Value)
{
    /// <summary>
    /// Number of differing bits. 0 means visually indistinguishable; small
    /// values mean a re-encode or a light edit; larger values diverge quickly.
    /// </summary>
    public int DistanceTo(PerceptualHash other) => BitOperations.PopCount(Value ^ other.Value);

    public static PerceptualHash Parse(string hex) =>
        new(ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    /// <summary>
    /// The difference hash of a greyscale image: each of 64 bits records whether
    /// a pixel is brighter than the one to its right.
    /// </summary>
    /// <remarks>
    /// Relative comparisons are what make it perceptual. Absolute brightness
    /// changes with re-encoding, resizing and exposure tweaks; the direction of
    /// the step between neighbouring pixels survives all three, which is why the
    /// same photo saved twice lands within a few bits of itself.
    ///
    /// <para>The image is box-averaged down to 9x8 first - one column wider than
    /// the 8 bits per row, because 8 comparisons need 9 pixels. Averaging rather
    /// than sampling means a single stray pixel cannot flip a bit.</para>
    /// </remarks>
    /// <param name="pixels">Greyscale bytes, one per pixel, row-major.</param>
    public static PerceptualHash FromGreyscale(ReadOnlySpan<byte> pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        if (pixels.Length < width * height)
        {
            throw new ArgumentException(
                $"Expected at least {width * height} greyscale bytes, got {pixels.Length}.",
                nameof(pixels));
        }

        Span<int> reduced = stackalloc int[HashColumns * HashRows];
        Reduce(pixels, width, height, reduced);

        ulong value = 0;
        for (int row = 0; row < HashRows; row++)
        {
            for (int column = 0; column < HashRows; column++)
            {
                int here = reduced[(row * HashColumns) + column];
                int right = reduced[(row * HashColumns) + column + 1];

                value <<= 1;
                if (here > right)
                {
                    value |= 1;
                }
            }
        }

        return new PerceptualHash(value);
    }

    public static bool TryParse(string? hex, out PerceptualHash hash)
    {
        hash = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        if (!ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value))
        {
            return false;
        }

        hash = new PerceptualHash(value);
        return true;
    }

    public override string ToString() => Value.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>8 bits per row need 9 pixels to compare between.</summary>
    private const int HashColumns = 9;

    private const int HashRows = 8;

    /// <summary>
    /// Box-averages an arbitrary greyscale image down to <see cref="HashColumns"/>
    /// by <see cref="HashRows"/>, so the hash does not depend on the source size.
    /// </summary>
    private static void Reduce(
        ReadOnlySpan<byte> pixels, int width, int height, Span<int> reduced)
    {
        for (int row = 0; row < HashRows; row++)
        {
            int top = row * height / HashRows;
            int bottom = Math.Max(top + 1, (row + 1) * height / HashRows);

            for (int column = 0; column < HashColumns; column++)
            {
                int left = column * width / HashColumns;
                int right = Math.Max(left + 1, (column + 1) * width / HashColumns);

                int total = 0, counted = 0;
                for (int y = top; y < bottom; y++)
                {
                    for (int x = left; x < right; x++)
                    {
                        total += pixels[(y * width) + x];
                        counted++;
                    }
                }

                reduced[(row * HashColumns) + column] = total / counted;
            }
        }
    }
}
