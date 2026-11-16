using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Faces;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Reading a preview's pixels, including one planted in the Sync folder that
/// claims a size no preview has.
/// </summary>
public sealed class PreviewPixelsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"pg-preview-{Guid.NewGuid():N}");

    public PreviewPixelsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void APreviewThisAppWrote_ReadsExactlyAsADirectDecodeDoes()
    {
        string path = Write("normal-p.jpg", new JpegBitmapEncoder { QualityLevel = 82 },
            ThumbnailSizes.PreviewEdge, 768);

        BgrImage? read = PreviewPixels.Read(path);

        Assert.NotNull(read);
        Assert.Equal(ThumbnailSizes.PreviewEdge, read!.Width);
        Assert.Equal(768, read.Height);
        Assert.Equal(DecodeDirectly(path), read.Pixels);
    }

    [Fact]
    public void APictureAsLargeAsAPreviewMayBe_IsStillRead()
    {
        string path = Write("largest-p.jpg", new PngBitmapEncoder(), PreviewPixels.LargestEdge, 4);

        Assert.NotNull(PreviewPixels.Read(path));
    }

    [Theory]
    [InlineData(30_000, 4)]
    [InlineData(4, 30_000)]
    public void APlantedPictureClaimingAHugeSize_IsRefusedWithoutDecoding(int width, int height)
    {
        // Small on disk, enormous once decoded - and named like a preview,
        // because the codec goes by the bytes and not the extension.
        string path = Write("planted-p.jpg", new PngBitmapEncoder(), width, height);

        Assert.True(new FileInfo(path).Length < 1_000_000);
        Assert.Null(PreviewPixels.Read(path));
    }

    [Fact]
    public void AFileThatIsNotAPicture_IsUnread()
    {
        string path = Path.Combine(_root, "junk-p.jpg");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04]);

        Assert.Null(PreviewPixels.Read(path));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless; failing the test over it
            // would not be.
        }
    }

    private string Write(string name, BitmapEncoder encoder, int width, int height)
    {
        int stride = width * 3;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 7 % 251);
        }

        BitmapSource picture = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgr24, null, pixels, stride);
        encoder.Frames.Add(BitmapFrame.Create(picture));

        string path = Path.Combine(_root, name);
        using FileStream file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private static byte[] DecodeDirectly(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var decoded = new BitmapImage();
        decoded.BeginInit();
        decoded.StreamSource = stream;
        decoded.CacheOption = BitmapCacheOption.OnLoad;
        decoded.EndInit();

        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgr24, null, 0d);
        int stride = converted.PixelWidth * 3;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
