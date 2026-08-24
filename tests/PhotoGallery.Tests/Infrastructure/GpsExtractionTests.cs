using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Reads coordinates out of a real file, through the real codecs.
/// </summary>
/// <remarks>
/// Covers both readers: the preparing pass, which takes coordinates as a
/// by-product of decoding a picture it was reading anyway, and the locating
/// pass, which opens the header alone for the eleven thousand photographs the
/// preparing pass will never open again. They share their tag lookup, and one
/// test below holds them to the same answer so they cannot drift apart.
///
/// <para>Unlike the rest of the generator's tests this needs no share and no
/// environment variable: it writes the JPEG it then reads, so it runs
/// everywhere and cannot quietly skip.</para>
///
/// <para>Writing the fixture is not the point - a writer and a reader can agree
/// on the wrong place and pass each other. The point is <b>where the block
/// lands</b>, which is asserted directly: 34853 is <c>GPSInfoIFDPointer</c>, so
/// a block at <c>/app1/{ushort=0}/{ushort=34853}</c> is exactly where a camera
/// puts one. A reader that finds it there finds a camera's too.</para>
///
/// <para>This test exists because the first version of the extraction returned
/// nothing for every photograph on the machine, and the local library turned out
/// to carry no GPS at all - so a passing suite and an empty column looked
/// identical. That is the same shape as the bug that once cost 864 HEIC
/// photographs their dates.</para>
/// </remarks>
public sealed class GpsExtractionTests : IDisposable
{
    private readonly string _folder;
    private readonly WindowsThumbnailGenerator _generator = new();

    public GpsExtractionTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), $"pg-gps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_folder);
    }

    /// <summary>One EXIF rational: numerator low, denominator high.</summary>
    private static ulong Rational(uint numerator, uint denominator) =>
        ((ulong)denominator << 32) | numerator;

    [Fact]
    public async Task Generate_ReadsCoordinatesFromWhereACameraWritesThem()
    {
        // 3 25' 26.04" N, 101 47' 34.8" E.
        string path = WritePhoto(
            "N",
            [Rational(3, 1), Rational(25, 1), Rational(2604, 100)],
            "E",
            [Rational(101, 1), Rational(47, 1), Rational(348, 10)]);

        // The block is at GPSInfoIFDPointer, which is what makes the rest of
        // this test evidence about real photographs rather than about itself.
        Assert.True(
            HasQuery(path, "/app1/{ushort=0}/{ushort=34853}"),
            "the fixture did not put GPS where a camera does, so it proves nothing");

        GeneratedThumbnail? made = await _generator.GenerateAsync(path);

        Assert.NotNull(made);
        Assert.Equal(3.4239, made!.Latitude!.Value, 4);
        Assert.Equal(101.7930, made.Longitude!.Value, 4);
    }

    [Fact]
    public async Task Generate_HonoursTheSouthernAndWesternHemispheres()
    {
        string path = WritePhoto(
            "S",
            [Rational(37, 1), Rational(48, 1), Rational(49, 1)],
            "W",
            [Rational(122, 1), Rational(25, 1), Rational(10, 1)]);

        GeneratedThumbnail? made = await _generator.GenerateAsync(path);

        Assert.NotNull(made);
        Assert.True(made!.Latitude < 0, "a southern latitude came back positive");
        Assert.True(made.Longitude < 0, "a western longitude came back positive");
    }

    [Fact]
    public async Task Generate_LeavesAPhotographWithNoGpsBlockUnplaced()
    {
        // The 61%. This must be null rather than zero, or every camera without a
        // receiver ends up in the Gulf of Guinea and gets a place name.
        string path = WritePhoto(null, null, null, null);

        GeneratedThumbnail? made = await _generator.GenerateAsync(path);

        Assert.NotNull(made);
        Assert.Null(made!.Latitude);
        Assert.Null(made.Longitude);
    }

    [Fact]
    public void Read_FindsCoordinatesWithoutDecodingThePicture()
    {
        string path = WritePhoto(
            "N",
            [Rational(3, 1), Rational(25, 1), Rational(2604, 100)],
            "E",
            [Rational(101, 1), Rational(47, 1), Rational(348, 10)]);

        CoordinateReading reading = new ExifOriginalCoordinates().Read(path);

        Assert.Equal(CoordinateOutcome.Found, reading.Outcome);
        Assert.Equal(3.4239, reading.Latitude, 4);
        Assert.Equal(101.7930, reading.Longitude, 4);
    }

    /// <summary>
    /// The cheap reader and the expensive one must not be able to disagree.
    /// </summary>
    /// <remarks>
    /// They share <see cref="GpsCoordinates"/> and <see cref="ExifQueries"/>
    /// today, but they open the file differently - one from a byte array it has
    /// already read whole, the other from a live stream it never finishes
    /// reading. That difference is exactly the kind that produces a reader which
    /// works on small files and quietly returns nothing on large ones.
    /// </remarks>
    [Fact]
    public async Task Read_AgreesWithThePassThatPreparesThePicture()
    {
        string path = WritePhoto(
            "S",
            [Rational(37, 1), Rational(48, 1), Rational(49, 1)],
            "W",
            [Rational(122, 1), Rational(25, 1), Rational(10, 1)]);

        GeneratedThumbnail? prepared = await _generator.GenerateAsync(path);
        CoordinateReading read = new ExifOriginalCoordinates().Read(path);

        Assert.NotNull(prepared);
        Assert.Equal(CoordinateOutcome.Found, read.Outcome);
        Assert.Equal(prepared!.Latitude!.Value, read.Latitude, 6);
        Assert.Equal(prepared.Longitude!.Value, read.Longitude, 6);
    }

    /// <summary>
    /// A camera with no receiver, which is a settled answer.
    /// </summary>
    /// <remarks>
    /// The distinction this and the next test draw is the one the whole pass
    /// rests on. "None" is written down and never asked again; "Unreadable" is
    /// not written down at all. Getting them the wrong way round either leaves
    /// nine thousand files re-read for ever, or leaves a photograph on a share
    /// that was briefly away unplaced permanently.
    /// </remarks>
    [Fact]
    public void Read_SaysNoneForAPhotographThatSimplyHasNoGps()
    {
        string path = WritePhoto(null, null, null, null);

        CoordinateReading reading = new ExifOriginalCoordinates().Read(path);

        Assert.Equal(CoordinateOutcome.None, reading.Outcome);
        Assert.True(reading.IsSettled);
    }

    [Fact]
    public void Read_SaysUnreadableForAFileThatCannotBeOpened()
    {
        CoordinateReading reading = new ExifOriginalCoordinates()
            .Read(Path.Combine(_folder, "no-such-file.jpg"));

        Assert.Equal(CoordinateOutcome.Unreadable, reading.Outcome);
        Assert.False(reading.IsSettled);
    }

    [Fact]
    public void Read_SaysUnreadableForSomethingThatIsNotAPicture()
    {
        string path = Path.Combine(_folder, "notes.jpg");
        File.WriteAllText(path, "this is not a JPEG");

        Assert.Equal(
            CoordinateOutcome.Unreadable, new ExifOriginalCoordinates().Read(path).Outcome);
    }

    /// <summary>
    /// Whether the file holds that query, asked while its stream is still open.
    /// </summary>
    /// <remarks>
    /// The stream has to outlive the question. A delay-created frame reads from
    /// it lazily, so a helper that opened the file, built the frame and returned
    /// its metadata would be asking a decoder whose bytes had gone - and the
    /// answer to every query is then a quiet "no", which is exactly what this
    /// test is here to catch.
    /// </remarks>
    private static bool HasQuery(string path, string query)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapFrame frame = BitmapFrame.Create(
            stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);

        return frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery(query);
    }

    private string WritePhoto(
        string? latitudeRef, ulong[]? latitude, string? longitudeRef, ulong[]? longitude)
    {
        string path = Path.Combine(_folder, $"{Guid.NewGuid():N}.jpg");
        var metadata = new BitmapMetadata("jpg");

        if (latitude is not null && longitude is not null)
        {
            metadata.SetQuery("/app1/ifd/gps/{ushort=1}", latitudeRef!);
            metadata.SetQuery("/app1/ifd/gps/{ushort=2}", latitude);
            metadata.SetQuery("/app1/ifd/gps/{ushort=3}", longitudeRef!);
            metadata.SetQuery("/app1/ifd/gps/{ushort=4}", longitude);
        }
        else
        {
            // Something, so the file still carries an APP1 block and the test
            // is about the absent GPS rather than about absent metadata.
            metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)1);
        }

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Blank(), null, metadata, null));

        using FileStream output = File.Create(path);
        encoder.Save(output);

        return path;
    }

    private static BitmapSource Blank() =>
        BitmapSource.Create(
            16, 16, 96, 96, PixelFormats.Rgb24, null, new byte[16 * 16 * 3], 16 * 3);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
