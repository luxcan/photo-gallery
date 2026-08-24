using PhotoGallery.Infrastructure.Faces;

namespace PhotoGallery.Tests.Infrastructure;

public sealed class BgrImageTests
{
    [Fact]
    public void Resize_PlacesSamplesAtPixelCentres()
    {
        // Two pixels widened to four. With the half-pixel convention the outer
        // two land outside the source and clamp to its ends, and the inner two
        // sit a quarter and three quarters of the way across.
        //
        // Dropping the half-pixel terms would give 0, 100, 200, 200 instead -
        // a shift small enough to look right in a picture and large enough to
        // move a landmark, which is what decides how a face is aligned.
        var source = new BgrImage([0, 0, 0, 200, 200, 200], width: 2, height: 1);

        BgrImage widened = source.Resize(4, 1);

        Assert.Equal([0, 50, 150, 200], Channel(widened, 0));
    }

    [Fact]
    public void Resize_AveragesThePixelsItCombines()
    {
        var source = new BgrImage(
            [0, 0, 0, 100, 100, 100, 200, 200, 200, 40, 40, 40], width: 4, height: 1);

        BgrImage halved = source.Resize(2, 1);

        Assert.Equal([50, 120], Channel(halved, 0));
    }

    [Fact]
    public void Resize_LeavesAFlatColourFlat()
    {
        byte[] pixels = new byte[8 * 8 * BgrImage.BytesPerPixel];
        Array.Fill(pixels, (byte)77);

        BgrImage resized = new BgrImage(pixels, 8, 8).Resize(19, 5);

        Assert.All(resized.Pixels, value => Assert.Equal(77, value));
    }

    [Fact]
    public void Sample_OutsideThePictureIsBlack()
    {
        var image = new BgrImage([9, 9, 9], width: 1, height: 1);
        Span<byte> pixel = stackalloc byte[BgrImage.BytesPerPixel];

        image.Sample(0, 0, pixel);
        Assert.Equal(9, pixel[0]);

        // A face at the edge of a photograph has a crop that runs off it, and
        // that has to fill with something rather than throw or wrap around.
        image.Sample(-1, 0, pixel);
        Assert.Equal(0, pixel[0]);
        image.Sample(0, 1, pixel);
        Assert.Equal(0, pixel[0]);
    }

    [Fact]
    public void Constructor_RefusesABufferThatDoesNotFit()
    {
        Assert.Throws<ArgumentException>(() => new BgrImage(new byte[5], width: 2, height: 1));
    }

    private static int[] Channel(BgrImage image, int channel) =>
        [.. Enumerable
            .Range(0, image.Width * image.Height)
            .Select(i => (int)image.Pixels[(i * BgrImage.BytesPerPixel) + channel])];
}
