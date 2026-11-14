using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Infrastructure.Imaging;

/// <summary>
/// Sharpness and exposure read from a cached preview, with no model.
/// </summary>
/// <remarks>
/// Sharpness is the variance of the Laplacian: how much fine edge detail
/// survives. It is only ever compared between frames of one burst, which show
/// the same scene, so the scene's own texture cancels out and what is left is
/// the shake or the missed focus (PRP 14).
///
/// <para>Measured on brightness alone, after a very light blur. Sensor grain at
/// dusk or indoors also reads as fine detail, and without the blur a noisier
/// frame would look sharper than a clean one; one pass of the smallest binomial
/// kernel removes most of the grain and little of a real edge.</para>
/// </remarks>
public static class PreviewQualityMeter
{
    /// <summary>How many tiles the longer side of a picture is cut into.</summary>
    public const int TilesAcross = 8;

    /// <summary>A pixel at least this bright is blown white.</summary>
    /// <remarks>
    /// A little below 255 because a preview is a JPEG: a blown sky comes back
    /// from the encoder as 252 to 255, not as 255 everywhere.
    /// </remarks>
    public const int ClippedHighAtLeast = 250;

    /// <summary>A pixel at most this bright is crushed black, for the same reason.</summary>
    public const int ClippedLowAtMost = 5;

    /// <summary>
    /// The longer edge every preview is brought down to before its sharpness is
    /// read, so copies of different sizes are compared at one size.
    /// </summary>
    /// <remarks>
    /// Without it a smaller re-saved copy has a smaller preview, and the same
    /// detail packed into fewer pixels reads as a sharper picture. Chosen from
    /// the phase 1 pairs (PRP 14): below the smallest preview in the library
    /// (828), and where the plainly shaken frames stand furthest above both the
    /// same frame at two sizes and the messaging-app re-saves. Lower than this
    /// and the shaken frames start to tie; higher and the re-saves start to
    /// win.
    /// </remarks>
    public const int CommonEdge = 640;

    /// <summary>
    /// The preview brought down to <see cref="CommonEdge"/> on its longer side,
    /// and the scale that took it there; null when it is already smaller.
    /// </summary>
    /// <remarks>
    /// Never enlarged. Enlarging cannot add detail, so a small copy would read
    /// as blurred and lose on a resampling artefact; a preview below the common
    /// size has no sharpness instead, and the ladder leaves it to the other
    /// rungs. The shrink is the area-averaging bicubic the content search
    /// already uses, which averages the pixels in between rather than skipping
    /// them.
    /// </remarks>
    public static (BgrImage Image, double Scale)? AtCommonSize(BgrImage preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        int longer = Math.Max(preview.Width, preview.Height);
        if (longer < CommonEdge)
        {
            return null;
        }

        if (longer == CommonEdge)
        {
            return (preview, 1d);
        }

        double scale = (double)CommonEdge / longer;
        int width = Math.Max(1, (int)Math.Round(preview.Width * scale));
        int height = Math.Max(1, (int)Math.Round(preview.Height * scale));
        return (preview.ResizeBicubic(width, height), scale);
    }

    /// <summary>A face box in a preview's pixels, carried to the same face at another scale.</summary>
    /// <remarks>Rounded outwards, so a small face never shrinks to nothing.</remarks>
    public static FaceBounds Scaled(FaceBounds face, double scale) =>
        new(
            (int)Math.Floor(face.X * scale),
            (int)Math.Floor(face.Y * scale),
            (int)Math.Ceiling(face.Width * scale),
            (int)Math.Ceiling(face.Height * scale));

    /// <summary>
    /// Sharpness over the faces that matter, or null when none of the boxes
    /// holds a pixel that can be measured.
    /// </summary>
    /// <remarks>
    /// All the boxes' pixels pooled into one variance, each pixel once however
    /// many boxes overlap it. A sharp background behind a blurred child is not
    /// a sharp photograph, so where there are faces only the faces count.
    /// </remarks>
    public static double? FaceSharpness(BgrImage preview, IReadOnlyList<FaceBounds> faces)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(faces);

        if (faces.Count == 0)
        {
            return null;
        }

        int width = preview.Width, height = preview.Height;
        float[] laplacian = Laplacian(Smooth(Luma(preview), width, height), width, height);
        bool[] counted = new bool[width * height];
        var spread = new Spread();

        foreach (FaceBounds face in faces)
        {
            // Only the inside of the picture has a Laplacian: its edge pixels
            // are missing a neighbour.
            int left = Math.Max(1, face.X), top = Math.Max(1, face.Y);
            int right = Math.Min(width - 1, face.X + face.Width);
            int bottom = Math.Min(height - 1, face.Y + face.Height);

            for (int y = top; y < bottom; y++)
            {
                for (int x = left; x < right; x++)
                {
                    int at = (y * width) + x;
                    if (!counted[at])
                    {
                        counted[at] = true;
                        spread.Add(laplacian[at]);
                    }
                }
            }
        }

        return spread.Count == 0 ? null : spread.Variance;
    }

    /// <summary>Sharpness over the most detailed quarter of the picture.</summary>
    /// <remarks>
    /// The picture is cut into a grid of tiles and only the top quarter of them
    /// by detail is scored, as the mean of their variances. A frame that is two
    /// thirds sky would otherwise average the sky's flat nothing into the
    /// score, and a shake along the ridge line would hardly move it.
    /// </remarks>
    public static double SceneSharpness(BgrImage preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        int width = preview.Width, height = preview.Height;
        if (width < 3 || height < 3)
        {
            return 0d;
        }

        float[] laplacian = Laplacian(Smooth(Luma(preview), width, height), width, height);

        // Square-ish tiles: TilesAcross on the longer side, as many as fit the
        // same size on the shorter one.
        int longer = Math.Max(width, height), shorter = Math.Min(width, height);
        int tilesShort = Math.Max(1, (int)Math.Round(TilesAcross * (double)shorter / longer));
        int columns = width >= height ? TilesAcross : tilesShort;
        int rows = width >= height ? tilesShort : TilesAcross;

        var tiles = new List<double>(columns * rows);
        for (int row = 0; row < rows; row++)
        {
            int top = Math.Max(1, row * height / rows);
            int bottom = Math.Min(height - 1, (row + 1) * height / rows);

            for (int column = 0; column < columns; column++)
            {
                int left = Math.Max(1, column * width / columns);
                int right = Math.Min(width - 1, (column + 1) * width / columns);

                var spread = new Spread();
                for (int y = top; y < bottom; y++)
                {
                    for (int x = left; x < right; x++)
                    {
                        spread.Add(laplacian[(y * width) + x]);
                    }
                }

                if (spread.Count > 0)
                {
                    tiles.Add(spread.Variance);
                }
            }
        }

        if (tiles.Count == 0)
        {
            return 0d;
        }

        tiles.Sort((a, b) => b.CompareTo(a));
        int kept = Math.Max(1, (tiles.Count + 3) / 4);
        return tiles.Take(kept).Average();
    }

    /// <summary>How much of the picture is blown white or crushed black.</summary>
    /// <remarks>
    /// Deliberately narrow: it catches the frame taken as the flash fired or as
    /// the camera was still adjusting, and says nothing about taste.
    /// </remarks>
    public static PreviewExposure Exposure(BgrImage preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        float[] luma = Luma(preview);
        int high = 0, low = 0;
        foreach (float value in luma)
        {
            // Rounded as an 8-bit grey would be, so the thresholds mean what a
            // greyscale histogram of the preview would show.
            int grey = (int)MathF.Round(value);
            if (grey >= ClippedHighAtLeast)
            {
                high++;
            }
            else if (grey <= ClippedLowAtMost)
            {
                low++;
            }
        }

        return new PreviewExposure((double)high / luma.Length, (double)low / luma.Length);
    }

    /// <summary>Brightness per pixel, with the Rec. 601 weights.</summary>
    private static float[] Luma(BgrImage image)
    {
        float[] luma = new float[image.Width * image.Height];
        byte[] pixels = image.Pixels;

        for (int i = 0; i < luma.Length; i++)
        {
            int at = i * BgrImage.BytesPerPixel;
            luma[i] = (0.114f * pixels[at]) + (0.587f * pixels[at + 1]) + (0.299f * pixels[at + 2]);
        }

        return luma;
    }

    /// <summary>
    /// One pass of the 3x3 binomial kernel, <c>[1 2 1] / 4</c> each way, with
    /// the edges repeated.
    /// </summary>
    private static float[] Smooth(float[] luma, int width, int height)
    {
        float[] across = new float[luma.Length];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float left = luma[row + Math.Max(0, x - 1)];
                float right = luma[row + Math.Min(width - 1, x + 1)];
                across[row + x] = (left + (2f * luma[row + x]) + right) / 4f;
            }
        }

        float[] smoothed = new float[luma.Length];
        for (int y = 0; y < height; y++)
        {
            int above = Math.Max(0, y - 1) * width;
            int below = Math.Min(height - 1, y + 1) * width;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                smoothed[row + x] = (across[above + x] + (2f * across[row + x]) + across[below + x]) / 4f;
            }
        }

        return smoothed;
    }

    /// <summary>
    /// The four-neighbour Laplacian, inside the picture only; the edge pixels
    /// are left at zero and never read.
    /// </summary>
    private static float[] Laplacian(float[] luma, int width, int height)
    {
        float[] laplacian = new float[luma.Length];
        for (int y = 1; y < height - 1; y++)
        {
            int row = y * width;
            for (int x = 1; x < width - 1; x++)
            {
                int at = row + x;
                laplacian[at] = luma[at - 1] + luma[at + 1] + luma[at - width] + luma[at + width]
                              - (4f * luma[at]);
            }
        }

        return laplacian;
    }

    /// <summary>A running variance, in doubles so a megapixel of squares stays exact enough.</summary>
    private struct Spread
    {
        private double _sum;
        private double _sumOfSquares;

        public int Count { get; private set; }

        public readonly double Variance
        {
            get
            {
                double mean = _sum / Count;
                return Math.Max(0d, (_sumOfSquares / Count) - (mean * mean));
            }
        }

        public void Add(float value)
        {
            _sum += value;
            _sumOfSquares += (double)value * value;
            Count++;
        }
    }
}
