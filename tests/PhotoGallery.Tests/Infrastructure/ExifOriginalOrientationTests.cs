using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The one place this app writes to a photograph the user owns, so what it must
/// not do matters more than what it does.
/// </summary>
public sealed class ExifOriginalOrientationTests : IDisposable
{
    private readonly string _root;
    private readonly IOriginalOrientation _orientation = new ExifOriginalOrientation();

    public ExifOriginalOrientationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-exif-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TryTurn_AdvancesTheTagOneStepRoundTheRing()
    {
        // Clockwise the orientations run 1 -> 6 -> 3 -> 8 -> 1, so a quarter turn
        // is one step. Getting this wrong mangles the picture in every program
        // that reads it.
        Assert.Equal(6, TurnAndRead(from: 1, degrees: 90));
        Assert.Equal(3, TurnAndRead(from: 6, degrees: 90));
        Assert.Equal(8, TurnAndRead(from: 3, degrees: 90));
        Assert.Equal(1, TurnAndRead(from: 8, degrees: 90));
    }

    [Fact]
    public void TryTurn_GoesTheOtherWayForAnAnticlockwiseQuarter() =>
        Assert.Equal(8, TurnAndRead(from: 1, degrees: -90));

    [Fact]
    public void TryTurn_ReadsAnIllegalZeroAsUpright() =>
        // Not a legal orientation, and some encoders write it anyway. Applying
        // it already treats it as upright, so composing has to agree.
        Assert.Equal(6, TurnAndRead(from: 0, degrees: 90));

    [Fact]
    public void TryTurn_LeavesTheFilesDatesExactlyWhereTheyWere()
    {
        // The gallery dates a photograph without a capture date by the earlier of
        // these, and the scan decides a file has been replaced by comparing them.
        // Letting them move would misplace the picture in the grid and throw away
        // everything derived from it, names included.
        string path = WriteJpeg(orientation: 1);

        var written = new DateTime(2014, 3, 11, 14, 22, 7, DateTimeKind.Utc);
        var created = new DateTime(2018, 1, 9, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);
        File.SetCreationTimeUtc(path, created);

        Assert.True(_orientation.TryTurn(path, 90));

        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        Assert.Equal(created, File.GetCreationTimeUtc(path));
    }

    [Fact]
    public void TryTurn_ChangesNothingButTheTag()
    {
        // What "lossless" means here, checked rather than claimed: the file does
        // not grow, and the compressed picture inside it is untouched.
        string path = WriteJpeg(orientation: 1);
        byte[] before = File.ReadAllBytes(path);

        Assert.True(_orientation.TryTurn(path, 180));

        byte[] after = File.ReadAllBytes(path);
        Assert.Equal(before.Length, after.Length);

        int changed = before.Where((b, i) => b != after[i]).Count();
        Assert.True(changed <= 4, $"{changed} bytes changed; only the tag should have");

        Assert.Equal(Pixels(before), Pixels(after));
    }

    [Fact]
    public void TryTurn_RefusesAFileWithNoTagRatherThanRewritingIt()
    {
        // Adding a tag means growing the header and shifting the whole file.
        // Refusing is what keeps this lossless; those pictures are corrected in
        // the app's own copies instead.
        string path = WriteJpeg(orientation: null);
        byte[] before = File.ReadAllBytes(path);

        Assert.False(_orientation.TryTurn(path, 90));

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void TryTurn_RefusesAMirroredOrientationItWillNotReasonAbout()
    {
        // 2, 4, 5 and 7 are flips. Composing a rotation with a flip is easy to
        // get subtly wrong, and being wrong means quietly mangling a photograph.
        foreach (ushort mirrored in new ushort[] { 2, 4, 5, 7 })
        {
            Assert.False(_orientation.TryTurn(WriteJpeg(mirrored), 90));
        }
    }

    [Fact]
    public void TryTurn_RefusesAnythingThatIsNotAQuarterTurn()
    {
        string path = WriteJpeg(orientation: 1);

        Assert.False(_orientation.TryTurn(path, 0));
        Assert.False(_orientation.TryTurn(path, 45));
        Assert.False(_orientation.TryTurn(path, 360));
    }

    [Fact]
    public void TryTurn_RefusesAFileThatIsNotThere() =>
        Assert.False(_orientation.TryTurn(Path.Combine(_root, "gone.jpg"), 90));

    private int TurnAndRead(int from, int degrees)
    {
        string path = WriteJpeg((ushort)from);
        Assert.True(_orientation.TryTurn(path, degrees), $"the write was refused for {from}");
        return Convert.ToInt32(ReadOrientation(path));
    }

    /// <summary>
    /// A real JPEG, with room reserved for its metadata to be edited.
    /// </summary>
    /// <remarks>
    /// The padding is the whole point of the distinction under test: a file that
    /// has it can be told a new orientation in place, and a file that does not
    /// cannot. Written explicitly here so both cases can be built on purpose.
    /// </remarks>
    private string WriteJpeg(ushort? orientation)
    {
        byte[] pixels = new byte[60 * 40 * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i % 251);
        }

        BitmapSource picture = BitmapSource.Create(
            60, 40, 96, 96, PixelFormats.Rgb24, null, pixels, 60 * 3);

        var metadata = new BitmapMetadata("jpg");
        if (orientation is ushort value)
        {
            metadata.SetQuery("/app1/ifd/PaddingSchema:Padding", (uint)2048);
            metadata.SetQuery("/app1/ifd/{ushort=274}", value);
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(picture, null, metadata, null));

        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.jpg");
        using (FileStream file = File.Create(path))
        {
            encoder.Save(file);
        }

        return path;
    }

    private static object? ReadOrientation(string path)
    {
        using FileStream file = File.OpenRead(path);
        BitmapDecoder decoder = BitmapDecoder.Create(
            file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        return (decoder.Frames[0].Metadata as BitmapMetadata)?.GetQuery("/app1/ifd/{ushort=274}");
    }

    /// <summary>The decoded picture, so "the pixels did not move" can be asserted.</summary>
    private static byte[] Pixels(byte[] jpeg)
    {
        using var source = new MemoryStream(jpeg, writable: false);
        BitmapFrame frame = BitmapDecoder.Create(
            source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];

        int stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
        byte[] bytes = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(bytes, stride, 0);
        return bytes;
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
