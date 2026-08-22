using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The alignment is the part of the face port that fails without saying so: a
/// crop that is slightly wrong still produces perfectly reasonable-looking
/// embeddings, which then match the wrong people. Everything here is closed
/// form, so the expected answers are arithmetic rather than judgement.
/// </summary>
public sealed class FaceAlignerTests
{
    /// <summary>
    /// Repeated here on purpose. If the constants in the aligner are ever edited
    /// this test fails, which is the point - they are the recognition model's
    /// training layout and are not ours to adjust.
    /// </summary>
    private static readonly float[] s_template =
    [
        38.2946f, 51.6963f,
        73.5318f, 51.5014f,
        56.0252f, 71.7366f,
        41.5493f, 92.3655f,
        70.7299f, 92.2041f,
    ];

    [Fact]
    public void Estimate_OfTheTemplateItselfIsTheIdentity()
    {
        Assert.True(FaceAligner.TryEstimate(s_template, out SimilarityTransform transform));

        Assert.Equal(1f, transform.A, 4);
        Assert.Equal(0f, transform.B, 4);
        Assert.Equal(0f, transform.TranslateX, 3);
        Assert.Equal(0f, transform.TranslateY, 3);
    }

    [Fact]
    public void Estimate_UndoesAPureScale()
    {
        Assert.True(FaceAligner.TryEstimate(Scaled(s_template, 2f), out SimilarityTransform transform));

        Assert.Equal(0.5f, transform.Scale, 4);
        Assert.Equal(0f, transform.B, 4);
    }

    [Fact]
    public void Estimate_UndoesAPureTranslation()
    {
        Assert.True(FaceAligner.TryEstimate(
            Translated(s_template, 10f, -20f), out SimilarityTransform transform));

        Assert.Equal(1f, transform.Scale, 4);
        Assert.Equal(-10f, transform.TranslateX, 3);
        Assert.Equal(20f, transform.TranslateY, 3);
    }

    [Theory]
    [InlineData(30f)]
    [InlineData(-45f)]
    [InlineData(170f)]
    public void Estimate_PutsARotatedFaceBackOnTheTemplate(float degrees)
    {
        float[] rotated = Rotated(s_template, degrees);

        Assert.True(FaceAligner.TryEstimate(rotated, out SimilarityTransform transform));
        Assert.Equal(1f, transform.Scale, 4);

        for (int point = 0; point < FaceAligner.LandmarkCount; point++)
        {
            (float x, float y) = transform.Apply(rotated[point * 2], rotated[(point * 2) + 1]);
            Assert.Equal(s_template[point * 2], x, 2);
            Assert.Equal(s_template[(point * 2) + 1], y, 2);
        }
    }

    [Fact]
    public void Estimate_FitsWhatItCannotMatchExactly()
    {
        // Real landmarks are never an exact similarity of the template, so the
        // fit has to be a least-squares one rather than an exact solve. Nudging
        // one point must move the answer a little and break nothing.
        float[] nudged = [.. s_template];
        nudged[0] += 3f;

        Assert.True(FaceAligner.TryEstimate(nudged, out SimilarityTransform transform));
        Assert.InRange(transform.Scale, 0.9f, 1.1f);
    }

    [Fact]
    public void Invert_ReversesTheTransform()
    {
        var transform = new SimilarityTransform(1.5f, -0.7f, 12f, -4f);
        Assert.True(transform.TryInvert(out SimilarityTransform inverse));

        (float x, float y) = transform.Apply(17f, 23f);
        (float backX, float backY) = inverse.Apply(x, y);

        Assert.Equal(17f, backX, 3);
        Assert.Equal(23f, backY, 3);
    }

    [Fact]
    public void Align_OfAFaceAlreadyOnTheTemplateCopiesThePixels()
    {
        // The transform is the identity here, so every destination pixel lands
        // exactly on a source pixel and the crop must come back untouched. Any
        // half-pixel error in the warp shows up immediately as a blur.
        BgrImage pattern = Pattern(FaceAligner.CropSize, FaceAligner.CropSize);

        BgrImage? crop = FaceAligner.Align(pattern, s_template);

        Assert.NotNull(crop);
        Assert.Equal(pattern.Pixels, crop.Pixels);
    }

    [Fact]
    public void Align_CutsTheRightRegionOutOfALargerPicture()
    {
        BgrImage pattern = Pattern(FaceAligner.CropSize, FaceAligner.CropSize);
        BgrImage canvas = Paste(pattern, 200, 180, atX: 40, atY: 50);

        BgrImage? crop = FaceAligner.Align(canvas, Translated(s_template, 40f, 50f));

        Assert.NotNull(crop);
        Assert.Equal(pattern.Pixels, crop.Pixels);
    }

    [Fact]
    public void Align_OfAFaceOverTheEdgeFillsWithBlackRatherThanFailing()
    {
        BgrImage pattern = Pattern(FaceAligner.CropSize, FaceAligner.CropSize);

        BgrImage? crop = FaceAligner.Align(pattern, Translated(s_template, 60f, 0f));

        Assert.NotNull(crop);
        Assert.Equal(FaceAligner.CropSize, crop.Width);

        // The right-hand strip came from outside the picture.
        int lastPixel = ((FaceAligner.CropSize * FaceAligner.CropSize) - 1) * BgrImage.BytesPerPixel;
        Assert.Equal(0, crop.Pixels[lastPixel]);
    }

    [Fact]
    public void Estimate_RefusesLandmarksThatAreAllInOnePlace()
    {
        float[] degenerate = [.. Enumerable.Repeat(10f, FaceAligner.LandmarkCount * 2)];

        Assert.False(FaceAligner.TryEstimate(degenerate, out _));
        Assert.Null(FaceAligner.Align(Pattern(32, 32), degenerate));
    }

    [Fact]
    public void Estimate_RefusesTheWrongNumberOfPoints()
    {
        Assert.False(FaceAligner.TryEstimate([1f, 2f, 3f, 4f], out _));
    }

    private static float[] Scaled(float[] points, float by) =>
        [.. points.Select(value => value * by)];

    private static float[] Translated(float[] points, float x, float y) =>
        [.. points.Select((value, index) => value + (index % 2 == 0 ? x : y))];

    private static float[] Rotated(float[] points, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        float centreX = 0f, centreY = 0f;
        for (int point = 0; point < FaceAligner.LandmarkCount; point++)
        {
            centreX += points[point * 2] / FaceAligner.LandmarkCount;
            centreY += points[(point * 2) + 1] / FaceAligner.LandmarkCount;
        }

        float[] rotated = new float[points.Length];
        for (int point = 0; point < FaceAligner.LandmarkCount; point++)
        {
            float x = points[point * 2] - centreX;
            float y = points[(point * 2) + 1] - centreY;
            rotated[point * 2] = (cos * x) - (sin * y) + centreX;
            rotated[(point * 2) + 1] = (sin * x) + (cos * y) + centreY;
        }

        return rotated;
    }

    private static BgrImage Pattern(int width, int height)
    {
        byte[] pixels = new byte[width * height * BgrImage.BytesPerPixel];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * BgrImage.BytesPerPixel] = (byte)(i % 251);
            pixels[(i * BgrImage.BytesPerPixel) + 1] = (byte)((i * 7) % 253);
            pixels[(i * BgrImage.BytesPerPixel) + 2] = (byte)((i * 13) % 249);
        }

        return new BgrImage(pixels, width, height);
    }

    private static BgrImage Paste(BgrImage source, int width, int height, int atX, int atY)
    {
        byte[] pixels = new byte[width * height * BgrImage.BytesPerPixel];
        for (int y = 0; y < source.Height; y++)
        {
            Array.Copy(
                source.Pixels,
                y * source.Width * BgrImage.BytesPerPixel,
                pixels,
                (((y + atY) * width) + atX) * BgrImage.BytesPerPixel,
                source.Width * BgrImage.BytesPerPixel);
        }

        return new BgrImage(pixels, width, height);
    }
}
