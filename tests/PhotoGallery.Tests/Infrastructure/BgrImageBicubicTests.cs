using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The second resampler, and why it is a second one.
/// </summary>
/// <remarks>
/// Neither of these can be checked by looking. A resampler that is subtly wrong
/// produces a picture that is merely a little soft, and the model fed on it
/// returns confident answers about the wrong thing - so what is asserted here is
/// the behaviour each convention is chosen for, not a golden image.
/// </remarks>
public sealed class BgrImageBicubicTests
{
    [Fact]
    public void ResizeBicubic_ProducesThePictureItWasAskedFor()
    {
        BgrImage source = Gradient(64, 48);

        BgrImage resized = source.ResizeBicubic(24, 24);

        Assert.Equal(24, resized.Width);
        Assert.Equal(24, resized.Height);
        Assert.Equal(24 * 24 * BgrImage.BytesPerPixel, resized.Pixels.Length);
    }

    [Fact]
    public void ResizeBicubic_KeepsAFlatColourFlat()
    {
        // The weights have to sum to one at every output pixel. If they do not,
        // a flat field develops edges - and every later difference would be
        // blamed on the model.
        BgrImage flat = Filled(70, 70, 40, 90, 200);

        BgrImage resized = flat.ResizeBicubic(224, 17);

        for (int i = 0; i < resized.Pixels.Length; i += BgrImage.BytesPerPixel)
        {
            Assert.Equal(40, resized.Pixels[i]);
            Assert.Equal(90, resized.Pixels[i + 1]);
            Assert.Equal(200, resized.Pixels[i + 2]);
        }
    }

    /// <summary>
    /// The widened kernel reaches a bright column from further away than the
    /// two-tap one, so more of the output knows the column is there.
    /// </summary>
    /// <remarks>
    /// Counted rather than asserted pixel by pixel, because a cubic kernel has
    /// negative lobes: one output pixel either side of the column comes out
    /// slightly below zero and clamps to nothing. That is real ringing rather
    /// than a mistake, and pinning an exact pixel would be pinning the lobe
    /// rather than the property that matters - which is simply that a kernel
    /// stepping over seven pixels in eight is not blind to them.
    /// </remarks>
    [Fact]
    public void ResizeBicubic_SeesMoreThanTheTwoTapPathWhenShrinkingHard()
    {
        BgrImage impulse = BrightColumn(256, 8, at: 100);

        int bicubic = Lit(impulse.ResizeBicubic(32, 8));
        int bilinear = Lit(impulse.Resize(32, 8));

        Assert.True(
            bicubic > bilinear,
            $"bicubic lit {bicubic} output pixels and bilinear {bilinear}; the widened "
            + "kernel is not widening");
    }

    /// <summary>
    /// The bilinear path still reads only its two nearest pixels.
    /// </summary>
    /// <remarks>
    /// It reproduces OpenCV, which samples two pixels however far it is
    /// shrinking, and the face detector's landmarks are compared against a
    /// reference that behaves the same way. If this ever stops holding, the two
    /// resamplers have been merged and the faces feature has changed without
    /// anyone saying so.
    /// </remarks>
    [Fact]
    public void Resize_StillReadsOnlyItsTwoNearestPixels()
    {
        BgrImage impulse = BrightColumn(256, 8, at: 100);

        BgrImage resized = impulse.Resize(32, 8);

        Assert.Equal(1, Lit(resized));
    }

    /// <summary>How many pixels of one row came out as anything but black.</summary>
    private static int Lit(BgrImage image)
    {
        int count = 0;
        for (int x = 0; x < image.Width; x++)
        {
            if (image.Pixels[((4 * image.Width) + x) * BgrImage.BytesPerPixel] > 0)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void CropCentre_TakesTheMiddle()
    {
        BgrImage source = Gradient(10, 10);

        BgrImage middle = source.CropCentre(4, 4);

        Assert.Equal(4, middle.Width);
        Assert.Equal(4, middle.Height);

        // Row 3, column 3 of the original sits at the top left of a centred 4x4.
        int expected = ((3 * 10) + 3) * BgrImage.BytesPerPixel;
        Assert.Equal(source.Pixels[expected], middle.Pixels[0]);
    }

    [Fact]
    public void CropCentre_RefusesToCutMoreThanThereIs()
    {
        BgrImage source = Gradient(8, 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.CropCentre(9, 4));
    }

    private static BgrImage Gradient(int width, int height)
    {
        byte[] pixels = new byte[width * height * BgrImage.BytesPerPixel];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 251);
        }

        return new BgrImage(pixels, width, height);
    }

    private static BgrImage Filled(int width, int height, byte blue, byte green, byte red)
    {
        byte[] pixels = new byte[width * height * BgrImage.BytesPerPixel];
        for (int i = 0; i < pixels.Length; i += BgrImage.BytesPerPixel)
        {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
        }

        return new BgrImage(pixels, width, height);
    }

    /// <summary>A dark field with one white column in it.</summary>
    private static BgrImage BrightColumn(int width, int height, int at)
    {
        byte[] pixels = new byte[width * height * BgrImage.BytesPerPixel];
        for (int y = 0; y < height; y++)
        {
            int index = (((y * width) + at) * BgrImage.BytesPerPixel);
            pixels[index] = 255;
            pixels[index + 1] = 255;
            pixels[index + 2] = 255;
        }

        return new BgrImage(pixels, width, height);
    }
}
