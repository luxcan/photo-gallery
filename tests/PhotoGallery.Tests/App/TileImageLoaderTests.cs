using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.App.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The loader is the one place a bad rendition may fail, so what it survives
/// matters more than what it produces.
/// </summary>
public sealed class TileImageLoaderTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemThumbnailStore _store;

    public TileImageLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-loader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();
        _store = new FileSystemThumbnailStore(workingFolder);
    }

    [Fact]
    public async Task LoadTile_ReturnsAFrozenImage()
    {
        string name = await SaveAsync("aa11");

        ImageSource? image = TileImageLoader.LoadTile(_store, name);

        Assert.NotNull(image);
        Assert.True(image!.IsFrozen, "an unfrozen image cannot cross to the UI thread");
    }

    [Fact]
    public void LoadTile_ReturnsNullForNothing()
    {
        Assert.Null(TileImageLoader.LoadTile(_store, null));
        Assert.Null(TileImageLoader.LoadTile(_store, string.Empty));
    }

    [Fact]
    public void LoadTile_ReturnsNullWhenTheFileIsNotThere() =>
        Assert.Null(TileImageLoader.LoadTile(_store, "ffffffffffffffffffffffffffffffff.jpg"));

    [Fact]
    public async Task LoadTile_ReturnsNullForAFileTruncatedInsideItsHeader()
    {
        // What a pass interrupted mid-write leaves behind. WIC raises
        // FileFormatException here, which derives from FormatException and not
        // from IOException - a filter that forgets it lets the exception escape
        // and takes the whole grid down.
        string name = await SaveAsync("bb22");
        string path = _store.ResolveTilePath(name);
        byte[] whole = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, whole[..200]);

        Assert.Null(TileImageLoader.LoadTile(_store, name));
    }

    [Fact]
    public async Task LoadTile_ReturnsNullForAnEmptyFile()
    {
        string name = await SaveAsync("cc33");
        await File.WriteAllBytesAsync(_store.ResolveTilePath(name), []);

        Assert.Null(TileImageLoader.LoadTile(_store, name));
    }

    [Fact]
    public async Task LoadTile_ReturnsNullForSomethingThatIsNotAnImage()
    {
        string name = await SaveAsync("dd44");
        await File.WriteAllTextAsync(_store.ResolveTilePath(name), "this is not a JPEG");

        Assert.Null(TileImageLoader.LoadTile(_store, name));
    }

    [Fact]
    public async Task LoadTile_DoesNotHoldTheFileOpen()
    {
        // If it did, the preparation pass could not overwrite a rendition the
        // grid happens to be showing.
        string name = await SaveAsync("ee55");
        ImageSource? image = TileImageLoader.LoadTile(_store, name);
        Assert.NotNull(image);

        await File.WriteAllBytesAsync(_store.ResolveTilePath(name), Jpeg(64, 64));
    }

    [Fact]
    public async Task LoadTile_SucceedsWhileAWriterHoldsTheFile()
    {
        // The pass writes with FileShare defaults; a reader that asked only for
        // FileShare.Read would be refused.
        string name = await SaveAsync("ff66");
        string path = _store.ResolveTilePath(name);

        using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            Assert.NotNull(TileImageLoader.LoadTile(_store, name));
        }
    }

    [Fact]
    public async Task LoadPreview_ReadsTheLargerRendition()
    {
        string name = await SaveAsync("aa77");

        ImageSource? preview = TileImageLoader.LoadPreview(_store, name);

        Assert.NotNull(preview);
        Assert.True(preview!.Width > TileImageLoader.LoadTile(_store, name)!.Width);
    }

    private async Task<string> SaveAsync(string seed) =>
        await _store.SaveAsync(new GeneratedThumbnail(
            Jpeg(80, 60),
            Jpeg(200, 150),
            1600,
            1200,
            null,
            new PerceptualHash(0),
            seed.PadRight(32, '0')));

    /// <summary>A real JPEG, so the codec is genuinely exercised.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        byte[] pixels = new byte[width * height * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 251);
        }

        BitmapSource source = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
