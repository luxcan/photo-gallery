using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Imaging;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Whether the cached picture still shows the original the way its file now
/// says, after another machine has written a new orientation tag into it.
/// </summary>
/// <remarks>
/// Against real files and the real decoder, because the answer is a comparison
/// of two pictures and a fake would only say what the test told it to.
/// </remarks>
public sealed class RenditionBehindOriginalTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemThumbnailStore _store;
    private readonly WindowsRenditionTurner _turner;
    private readonly ExifOriginalOrientation _file = new();

    public RenditionBehindOriginalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-behind-{Guid.NewGuid():N}");
        var working = new WorkingFolder(Path.Combine(_root, "working"));
        working.EnsureCreated();

        _store = new FileSystemThumbnailStore(working);
        _turner = new WindowsRenditionTurner(_store);
    }

    [Fact]
    public async Task AFileNobodyTurnedIsNotBehind()
    {
        (string original, string name) = await PreparedAsync();

        Assert.Equal(0, _turner.DegreesBehindOriginal(name, original, 0));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task AFileTurnedElsewhereIsBehindByThatTurn(int degrees)
    {
        (string original, string name) = await PreparedAsync();

        Assert.True(_file.TryTurn(original, degrees));

        Assert.Equal(degrees, _turner.DegreesBehindOriginal(name, original, 0));
    }

    [Fact]
    public async Task TheAppsOwnTurnCountsTowardsTheAnswer()
    {
        // The file now says a quarter clockwise, and this library also holds a
        // quarter anticlockwise of its own: together, upright as built.
        (string original, string name) = await PreparedAsync();
        Assert.True(_file.TryTurn(original, 90));

        Assert.Equal(0, _turner.DegreesBehindOriginal(name, original, 270));
    }

    [Fact]
    public async Task ACaughtUpPictureIsNotBehind()
    {
        (string original, string name) = await PreparedAsync();
        Assert.True(_file.TryTurn(original, 90));

        int behind = _turner.DegreesBehindOriginal(name, original, 0)!.Value;
        _turner.Turn(name, behind);

        Assert.Equal(0, _turner.DegreesBehindOriginal(name, original, 0));
    }

    [Fact]
    public async Task AnOriginalThatCannotBeRead_HasNoAnswer()
    {
        (_, string name) = await PreparedAsync();

        Assert.Null(_turner.DegreesBehindOriginal(name, Path.Combine(_root, "gone.jpg"), 0));
    }

    [Fact]
    public async Task AnOriginalTheDecoderCannotRead_IsNotBehind()
    {
        // HEIC on a machine without the extension, or a damaged file. Asking
        // again would hold its turn for ever.
        (_, string name) = await PreparedAsync();
        string still = Path.Combine(_root, "still.heic");
        await File.WriteAllBytesAsync(still, [0, 0, 0, 24, 102, 116, 121, 112]);

        Assert.Equal(0, _turner.DegreesBehindOriginal(name, still, 0));
    }

    [Fact]
    public async Task AVideo_IsNotBehind_WithoutReadingIt()
    {
        // The path does not exist, so any attempt to read it would answer null
        // and hold the turn. A clip over 2 GB cannot be read whole either.
        (_, string name) = await PreparedAsync();

        Assert.Equal(0, _turner.DegreesBehindOriginal(name, Path.Combine(_root, "gone.MOV"), 0));
    }

    [Fact]
    public async Task NoCachedPicture_IsNothingToTurn()
    {
        (string original, _) = await PreparedAsync();

        Assert.Equal(0, _turner.DegreesBehindOriginal("ffff.jpg", original, 0));
    }

    [Fact]
    public async Task APictureThatLooksAlikeEveryWayRound_IsLeftAlone()
    {
        // A flat grey wall. Any answer would be a guess.
        (string original, string name) = await PreparedAsync(flat: true);
        Assert.True(_file.TryTurn(original, 90));

        Assert.Equal(0, _turner.DegreesBehindOriginal(name, original, 0));
    }

    /// <summary>
    /// An original with an orientation tag that can be rewritten in place, and
    /// the renditions this app would have built from it.
    /// </summary>
    private async Task<(string Original, string Name)> PreparedAsync(bool flat = false)
    {
        const int width = 400, height = 300;
        byte[] pixels = new byte[width * height * 3];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // A slope and a bright corner, so no quarter turn looks like
                // another.
                int value = flat
                    ? 128
                    : (x * 150 / width) + (y * 60 / height)
                      + (x < width / 3 && y < height / 2 ? 40 : 0);

                int at = ((y * width) + x) * 3;
                pixels[at] = pixels[at + 1] = pixels[at + 2] = (byte)value;
            }
        }

        BitmapSource picture = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);

        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/PaddingSchema:Padding", (uint)2048);
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)1);

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(picture, null, metadata, null));

        string original = Path.Combine(_root, $"{Guid.NewGuid():N}.jpg");
        using (FileStream file = File.Create(original))
        {
            encoder.Save(file);
        }

        GeneratedThumbnail built = await new WindowsThumbnailGenerator().GenerateAsync(original)
            ?? throw new InvalidOperationException("the test picture did not decode");

        return (original, await _store.SaveAsync(built));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder; a decoder may not have let go of a file yet.
        }
    }
}
