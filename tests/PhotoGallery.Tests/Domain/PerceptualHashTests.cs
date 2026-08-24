using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.Domain;

public sealed class PerceptualHashTests
{
    [Fact]
    public void FromGreyscale_BrightnessFallingLeftToRight_SetsEveryBit()
    {
        // Every pixel is brighter than the one to its right, which is what a set
        // bit records.
        byte[] pixels = Render(64, 48, (x, _) => 1d - x);

        Assert.Equal(ulong.MaxValue, PerceptualHash.FromGreyscale(pixels, 64, 48).Value);
    }

    [Fact]
    public void FromGreyscale_BrightnessRisingLeftToRight_ClearsEveryBit()
    {
        byte[] pixels = Render(64, 48, (x, _) => x);

        Assert.Equal(0ul, PerceptualHash.FromGreyscale(pixels, 64, 48).Value);
    }

    [Fact]
    public void FromGreyscale_FlatImage_ClearsEveryBit()
    {
        // Equal is not brighter, so a blank wall hashes to nothing set.
        byte[] pixels = Render(20, 20, (_, _) => 0.5d);

        Assert.Equal(0ul, PerceptualHash.FromGreyscale(pixels, 20, 20).Value);
    }

    [Fact]
    public void FromGreyscale_SamePictureAtDifferentSizes_HashesTheSame()
    {
        // The whole point: a 4000px original and its 400px copy are the same
        // picture, so the hash cannot depend on resolution.
        static double Pattern(double x, double y) => (Math.Sin(x * 6) + Math.Cos(y * 4) + 2) / 4;

        PerceptualHash small = PerceptualHash.FromGreyscale(Render(40, 30, Pattern), 40, 30);
        PerceptualHash large = PerceptualHash.FromGreyscale(Render(400, 300, Pattern), 400, 300);

        Assert.Equal(small, large);
    }

    [Fact]
    public void FromGreyscale_DifferentPictures_HashDifferently()
    {
        PerceptualHash one = PerceptualHash.FromGreyscale(
            Render(64, 64, (x, y) => (Math.Sin(x * 5) + 1) / 2 * y), 64, 64);
        PerceptualHash other = PerceptualHash.FromGreyscale(
            Render(64, 64, (x, y) => (Math.Cos(y * 9) + 1) / 2 * x), 64, 64);

        Assert.NotEqual(one, other);
        Assert.True(one.DistanceTo(other) > 8, $"distance was only {one.DistanceTo(other)}");
    }

    [Fact]
    public void FromGreyscale_ImageSmallerThanTheHashGrid_StillProducesAHash()
    {
        // 4x4 is smaller than the 9x8 grid the hash reduces to. It must not throw
        // or divide by zero; a library of 20,000 files will contain an icon.
        byte[] pixels = Render(4, 4, (x, _) => 1d - x);

        Assert.NotEqual(0ul, PerceptualHash.FromGreyscale(pixels, 4, 4).Value);
    }

    [Fact]
    public void FromGreyscale_TooFewPixels_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => PerceptualHash.FromGreyscale(new byte[10], 16, 16));
    }

    [Fact]
    public void ToStringAndParse_RoundTrip()
    {
        var hash = new PerceptualHash(0xAEC3897E81D03EC3);

        Assert.Equal("aec3897e81d03ec3", hash.ToString());
        Assert.Equal(hash, PerceptualHash.Parse(hash.ToString()));
    }

    /// <summary>
    /// Paints an image from a function of normalised coordinates, so the same
    /// picture can be produced at any size.
    /// </summary>
    private static byte[] Render(int width, int height, Func<double, double, double> intensity)
    {
        byte[] pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double value = intensity((x + 0.5) / width, (y + 0.5) / height);
                pixels[(y * width) + x] = (byte)Math.Clamp(value * 255, 0, 255);
            }
        }

        return pixels;
    }
}
