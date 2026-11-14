using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Infrastructure.Faces;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Sharpness and exposure on synthetic pictures whose answers are known: the
/// same scene sharp, blurred, shaken, grainy and drawn at two sizes.
/// </summary>
public sealed class PreviewQualityMeterTests
{
    private const int Width = 1024;
    private const int Height = 768;

    [Fact]
    public void SceneSharpness_OfABlurredFrameIsClearlyBelowTheSharpOne()
    {
        BgrImage sharp = Scene(Width, Height);
        BgrImage blurred = BoxBlur(sharp, radius: 2);

        Assert.True(
            PreviewQualityMeter.SceneSharpness(sharp) > 3 * PreviewQualityMeter.SceneSharpness(blurred));
    }

    [Fact]
    public void SceneSharpness_OfAFlatPictureIsZero() =>
        Assert.Equal(0d, PreviewQualityMeter.SceneSharpness(Flat(Width, Height, 90)), 6);

    [Fact]
    public void SceneSharpness_IsNotDilutedByAnEmptySky()
    {
        // Two thirds sky over a sharp ridge scores as the ridge does: only the
        // most detailed quarter of the tiles is counted, and the ridge alone
        // fills more than a quarter.
        BgrImage ridge = Scene(Width, Height);
        BgrImage skyOverRidge = SkyOver(ridge, skyRows: Height * 2 / 3);

        double ratio = PreviewQualityMeter.SceneSharpness(skyOverRidge)
                     / PreviewQualityMeter.SceneSharpness(ridge);

        Assert.InRange(ratio, 0.8, 1.25);
    }

    [Fact]
    public void SceneSharpness_SeesAShakenRidgeUnderASky()
    {
        BgrImage sharp = SkyOver(Scene(Width, Height), skyRows: Height * 2 / 3);
        BgrImage shaken = SkyOver(MotionBlur(Scene(Width, Height), length: 9), skyRows: Height * 2 / 3);

        Assert.True(
            PreviewQualityMeter.SceneSharpness(sharp) > 2 * PreviewQualityMeter.SceneSharpness(shaken));
    }

    [Fact]
    public void SceneSharpness_OfOneFrameFromTwoOriginalsAtLeastThePreviewEdgeTies()
    {
        // Both originals are larger than the preview edge, so both previews are
        // drawn at that edge and must not differ on how they were resampled.
        // This does NOT cover an original smaller than the edge: its preview is
        // smaller too, and the test below shows those scores do not compare.
        BgrImage fromLarge = Scene(2048, 1536).ResizeBicubic(Width, Height);
        BgrImage fromSmaller = Scene(1300, 975).ResizeBicubic(Width, Height);

        double ratio = PreviewQualityMeter.SceneSharpness(fromLarge)
                     / PreviewQualityMeter.SceneSharpness(fromSmaller);

        Assert.InRange(ratio, 0.85, 1.18);
    }

    [Fact]
    public void SceneSharpness_OfOneFrameAtTwoPreviewSizesDoesNotTie()
    {
        // An original smaller than the preview edge keeps its own size as its
        // preview (the library's previews run from 473 to 1024 wide). The meter
        // does not resample, so the same frame scores differently at the two
        // sizes. The ladder must therefore compare sharpness only between copies
        // of equal pixel count, or a re-saved smaller copy is judged on its size.
        BgrImage atEdge = Scene(Width, Height);
        BgrImage atOwnSmallerSize = Scene(640, 480);

        double ratio = PreviewQualityMeter.SceneSharpness(atEdge)
                     / PreviewQualityMeter.SceneSharpness(atOwnSmallerSize);

        Assert.False(ratio is >= 0.85 and <= 1.18, $"ratio {ratio:F3} would read as a tie");
    }
    [Fact]
    public void AtCommonSize_OneFrameAtTwoPreviewSizesTies()
    {
        // The fix for the test above (PRP 14): every preview is brought down to
        // the common edge before it is read, so a smaller re-saved copy of the
        // same frame is not judged on its size.
        BgrImage atEdge = Scene(Width, Height);
        BgrImage resavedSmaller = atEdge.ResizeBicubic(820, 615);

        double ratio = CommonSharpness(atEdge) / CommonSharpness(resavedSmaller);

        Assert.InRange(ratio, 1d / KeeperPolicy.SharpnessMargin, KeeperPolicy.SharpnessMargin);
    }

    [Fact]
    public void AtCommonSize_AShakenFrameIsStillClearlyLessSharp()
    {
        BgrImage sharp = Scene(Width, Height);
        BgrImage shaken = MotionBlur(sharp, length: 7);

        Assert.True(CommonSharpness(sharp) > KeeperPolicy.SharpnessMargin * CommonSharpness(shaken));
    }

    [Fact]
    public void AtCommonSize_APreviewSmallerThanTheCommonEdgeIsNotEnlarged() =>
        Assert.Null(PreviewQualityMeter.AtCommonSize(Scene(PreviewQualityMeter.CommonEdge - 1, 400)));

    [Fact]
    public void AtCommonSize_BringsTheLongerEdgeToTheCommonEdge()
    {
        (BgrImage image, double scale) = PreviewQualityMeter.AtCommonSize(Scene(768, 1024))!.Value;

        Assert.Equal(PreviewQualityMeter.CommonEdge, image.Height);
        Assert.Equal(480, image.Width);
        Assert.Equal(0.625, scale, precision: 6);
    }

    [Fact]
    public void SceneSharpness_DoesNotRewardGrain()
    {
        // Sensor grain at dusk is fine detail too. A softer frame with grain on
        // it must not outscore a clean sharp one.
        BgrImage clean = Scene(Width, Height);
        BgrImage grainy = Grain(BoxBlur(clean, radius: 1), sigma: 8, seed: 7);

        Assert.True(
            PreviewQualityMeter.SceneSharpness(clean) > PreviewQualityMeter.SceneSharpness(grainy));
    }

    [Fact]
    public void FaceSharpness_ReadsTheFaceNotTheBackground()
    {
        // A sharp background behind a blurred child is not a sharp photograph.
        FaceBounds face = new(400, 250, 200, 240);
        BgrImage sharp = Scene(Width, Height);
        BgrImage blurred = BoxBlur(sharp, radius: 3);

        BgrImage sharpFace = Paste(blurred, sharp, face);
        BgrImage blurredFace = Paste(sharp, blurred, face);

        Assert.True(
            PreviewQualityMeter.FaceSharpness(sharpFace, [face])
            > 2 * PreviewQualityMeter.FaceSharpness(blurredFace, [face]));
        Assert.True(
            PreviewQualityMeter.SceneSharpness(blurredFace)
            > PreviewQualityMeter.SceneSharpness(sharpFace));
    }

    [Fact]
    public void FaceSharpness_WithNoFacesIsUnknown() =>
        Assert.Null(PreviewQualityMeter.FaceSharpness(Scene(Width, Height), []));

    [Fact]
    public void FaceSharpness_OfABoxOutsideThePictureIsUnknown() =>
        Assert.Null(PreviewQualityMeter.FaceSharpness(
            Scene(Width, Height), [new FaceBounds(Width + 10, 10, 50, 50)]));

    [Fact]
    public void Exposure_CountsBlownAndCrushedPixels()
    {
        // A quarter pure white, a quarter pure black, the rest mid grey.
        BgrImage picture = Flat(400, 400, 128);
        Fill(picture, new FaceBounds(0, 0, 400, 100), 255);
        Fill(picture, new FaceBounds(0, 300, 400, 100), 0);

        PreviewExposure exposure = PreviewQualityMeter.Exposure(picture);

        Assert.Equal(0.25, exposure.ClippedHigh, 6);
        Assert.Equal(0.25, exposure.ClippedLow, 6);
    }

    [Fact]
    public void Exposure_OfAWellExposedPictureIsNothingClipped()
    {
        PreviewExposure exposure = PreviewQualityMeter.Exposure(Flat(400, 300, 128));

        Assert.Equal(0d, exposure.ClippedHigh);
        Assert.Equal(0d, exposure.ClippedLow);
    }

    /// <summary>
    /// A busy scene drawn from continuous coordinates, so the same scene can be
    /// drawn at any size of the same shape: overlapping discs and bars of many
    /// sizes and tones, drawn at twice the size and averaged down so the edges
    /// are not aliased.
    /// </summary>
    /// <summary>Scene sharpness read the way the ladder reads it: at the common size.</summary>
    private static double CommonSharpness(BgrImage preview) =>
        PreviewQualityMeter.SceneSharpness(PreviewQualityMeter.AtCommonSize(preview)!.Value.Image);

    private static BgrImage Scene(int width, int height)
    {
        var random = new Random(1234);
        int fineWidth = width * 2, fineHeight = height * 2;
        byte[] fine = new byte[fineWidth * fineHeight];
        Array.Fill(fine, (byte)120);

        // Positions and sizes in widths of the picture, so a larger drawing is
        // the same scene and not a different crop of it.
        for (int shape = 0; shape < 400; shape++)
        {
            bool disc = random.Next(2) == 0;
            double centreX = random.NextDouble(), centreY = random.NextDouble() * height / width;
            double halfWidth = 0.004 + (random.NextDouble() * 0.04);
            double halfHeight = disc ? halfWidth : 0.002 + (random.NextDouble() * 0.012);
            byte tone = (byte)(30 + random.Next(200));

            int left = Math.Max(0, (int)((centreX - halfWidth) * fineWidth));
            int right = Math.Min(fineWidth - 1, (int)((centreX + halfWidth) * fineWidth) + 1);
            int top = Math.Max(0, (int)((centreY - halfHeight) * fineWidth));
            int bottom = Math.Min(fineHeight - 1, (int)((centreY + halfHeight) * fineWidth) + 1);

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    double dx = ((x + 0.5) / fineWidth) - centreX;
                    double dy = ((y + 0.5) / fineWidth) - centreY;
                    bool inside = disc
                        ? (dx * dx) + (dy * dy) < halfWidth * halfWidth
                        : Math.Abs(dx) < halfWidth && Math.Abs(dy) < halfHeight;
                    if (inside)
                    {
                        fine[(y * fineWidth) + x] = tone;
                    }
                }
            }
        }

        byte[] pixels = new byte[width * height * BgrImage.BytesPerPixel];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = (2 * y * fineWidth) + (2 * x);
                byte tone = (byte)((fine[at] + fine[at + 1] + fine[at + fineWidth] + fine[at + fineWidth + 1] + 2) / 4);
                pixels.AsSpan(((y * width) + x) * BgrImage.BytesPerPixel, BgrImage.BytesPerPixel).Fill(tone);
            }
        }

        return new BgrImage(pixels, width, height);
    }
    private static BgrImage Flat(int width, int height, byte tone)
    {
        byte[] pixels = new byte[width * height * BgrImage.BytesPerPixel];
        Array.Fill(pixels, tone);
        return new BgrImage(pixels, width, height);
    }

    private static void Fill(BgrImage image, FaceBounds area, byte tone)
    {
        for (int y = area.Y; y < area.Y + area.Height; y++)
        {
            int at = ((y * image.Width) + area.X) * BgrImage.BytesPerPixel;
            image.Pixels.AsSpan(at, area.Width * BgrImage.BytesPerPixel).Fill(tone);
        }
    }

    /// <summary>The top rows replaced by a smooth sky, lightening upwards.</summary>
    private static BgrImage SkyOver(BgrImage scene, int skyRows)
    {
        byte[] pixels = (byte[])scene.Pixels.Clone();
        for (int y = 0; y < skyRows; y++)
        {
            byte tone = (byte)(200 + (40 * (skyRows - y) / skyRows));
            pixels.AsSpan(y * scene.Width * BgrImage.BytesPerPixel, scene.Width * BgrImage.BytesPerPixel)
                .Fill(tone);
        }

        return new BgrImage(pixels, scene.Width, scene.Height);
    }

    private static BgrImage Paste(BgrImage background, BgrImage source, FaceBounds area)
    {
        byte[] pixels = (byte[])background.Pixels.Clone();
        for (int y = area.Y; y < area.Y + area.Height; y++)
        {
            int at = ((y * background.Width) + area.X) * BgrImage.BytesPerPixel;
            source.Pixels.AsSpan(at, area.Width * BgrImage.BytesPerPixel).CopyTo(pixels.AsSpan(at));
        }

        return new BgrImage(pixels, background.Width, background.Height);
    }

    private static BgrImage BoxBlur(BgrImage image, int radius) =>
        Average(Average(image, radius, horizontally: true), radius, horizontally: false);

    /// <summary>A horizontal shake: every pixel smeared along a line.</summary>
    private static BgrImage MotionBlur(BgrImage image, int length) =>
        Average(image, length / 2, horizontally: true);

    private static BgrImage Average(BgrImage image, int radius, bool horizontally)
    {
        byte[] result = new byte[image.Pixels.Length];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                for (int channel = 0; channel < BgrImage.BytesPerPixel; channel++)
                {
                    int sum = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sx = horizontally ? Math.Clamp(x + k, 0, image.Width - 1) : x;
                        int sy = horizontally ? y : Math.Clamp(y + k, 0, image.Height - 1);
                        sum += image.Pixels[(((sy * image.Width) + sx) * BgrImage.BytesPerPixel) + channel];
                    }

                    result[(((y * image.Width) + x) * BgrImage.BytesPerPixel) + channel] =
                        (byte)Math.Round(sum / (double)((2 * radius) + 1));
                }
            }
        }

        return new BgrImage(result, image.Width, image.Height);
    }

    /// <summary>Gaussian noise of the given spread, the same on every channel.</summary>
    private static BgrImage Grain(BgrImage image, double sigma, int seed)
    {
        var random = new Random(seed);
        byte[] pixels = (byte[])image.Pixels.Clone();
        for (int i = 0; i < pixels.Length; i += BgrImage.BytesPerPixel)
        {
            double u1 = 1d - random.NextDouble(), u2 = random.NextDouble();
            double noise = sigma * Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2);
            for (int channel = 0; channel < BgrImage.BytesPerPixel; channel++)
            {
                pixels[i + channel] = (byte)Math.Clamp(Math.Round(pixels[i + channel] + noise), 0, 255);
            }
        }

        return new BgrImage(pixels, image.Width, image.Height);
    }
}
