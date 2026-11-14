using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Imaging;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Reading which way up a clip's own file says it goes.
/// </summary>
/// <remarks>
/// Against files built byte by byte in <see cref="TestMp4"/>, laid out the way
/// the first real library's clips are: one video track, a version 0 header,
/// the movie box at the front or - in three clips of every four - behind the
/// film.
/// </remarks>
public sealed class Mp4VideoOrientationReaderTests : IDisposable
{
    private readonly string _root =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"pg-mp4-{Guid.NewGuid():N}")).FullName;

    private readonly Mp4VideoOrientationReader _reader = new();

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void EachQuarterTurnIsReadWithTheStoredSize(int degrees)
    {
        string clip = Write(TestMp4.FileType(), TestMp4.Movie(TestMp4.VideoTrack(degrees)), TestMp4.Film());

        Assert.Equal(new VideoOrientation(degrees, 1920, 1080), _reader.Read(clip));
    }

    [Fact]
    public void AMovieBoxBehindTheFilmIsFound()
    {
        string clip = Write(TestMp4.FileType(), TestMp4.Film(), TestMp4.Movie(TestMp4.VideoTrack(180)));

        Assert.Equal(180, _reader.Read(clip)?.Degrees);
    }

    [Fact]
    public void AFilmSizedInSixtyFourBitsIsSteppedOver()
    {
        string clip = Write(
            TestMp4.FileType(),
            TestMp4.LargeBox("mdat", new byte[4096]),
            TestMp4.Movie(TestMp4.VideoTrack(270)));

        Assert.Equal(270, _reader.Read(clip)?.Degrees);
    }

    [Fact]
    public void AMovieBoxSizedInSixtyFourBitsIsRead()
    {
        string clip = Write(
            TestMp4.FileType(),
            TestMp4.LargeBox("moov", TestMp4.Box("mvhd", new byte[100]), TestMp4.VideoTrack(90)));

        Assert.Equal(90, _reader.Read(clip)?.Degrees);
    }

    [Fact]
    public void AMovieBoxRunningToTheEndOfTheFileIsRead()
    {
        string clip = Write(
            TestMp4.FileType(),
            TestMp4.Film(),
            TestMp4.OpenEndedBox("moov", TestMp4.Box("mvhd", new byte[100]), TestMp4.VideoTrack(90)));

        Assert.Equal(90, _reader.Read(clip)?.Degrees);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void AVersionOneHeaderIsRead(int degrees)
    {
        // Wider times and length push the matrix twelve bytes further in.
        string clip = Write(
            TestMp4.FileType(),
            TestMp4.Movie(TestMp4.VideoTrack(degrees, 1280, 720, version: 1)));

        Assert.Equal(new VideoOrientation(degrees, 1280, 720), _reader.Read(clip));
    }

    [Fact]
    public void TheVideoTrackIsReadRatherThanTheSoundBeforeIt()
    {
        // A sound track's header is upright, so reading the first track would
        // call every clip the right way up.
        string clip = Write(
            TestMp4.FileType(),
            TestMp4.Movie(TestMp4.SoundTrack(), TestMp4.VideoTrack(90)));

        Assert.Equal(90, _reader.Read(clip)?.Degrees);
    }

    [Fact]
    public void AFileWithOnlySoundCannotSay()
    {
        string clip = Write(TestMp4.FileType(), TestMp4.Movie(TestMp4.SoundTrack()));

        Assert.Null(_reader.Read(clip));
    }

    [Theory]
    [InlineData(2 * TestMp4.One, 0, 0, 2 * TestMp4.One)]
    [InlineData(-TestMp4.One, 0, 0, TestMp4.One)]
    [InlineData(46341, 46341, -46341, 46341)]
    public void AMatrixThatIsNotAPlainQuarterTurnCannotSay(int a, int b, int c, int d)
    {
        // Scaled, mirrored, and an eighth of a turn. Drawing any of them as the
        // nearest quarter would be a guess dressed as an answer.
        string clip = Write(
            TestMp4.FileType(),
            TestMp4.Movie(TestMp4.Track("vide", TestMp4.TrackHeader(0, a, b, c, d, 1920, 1080))));

        Assert.Null(_reader.Read(clip));
    }

    [Fact]
    public void AFileWithNoMovieBoxCannotSay()
    {
        // What 42 of the library's .MOV files are: HEIC stills with a video's name.
        string clip = Write(TestMp4.FileType("heic"), TestMp4.Box("meta", new byte[64]), TestMp4.Film());

        Assert.Null(_reader.Read(clip));
    }

    [Fact]
    public void AFileCutShortCannotSay()
    {
        byte[] whole = TestMp4.File(TestMp4.FileType(), TestMp4.Film(), TestMp4.Movie(TestMp4.VideoTrack(90)));

        Assert.Null(_reader.Read(WriteBytes(whole[..^20])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1002)]
    public void ATinyOrEmptyFileCannotSay(int length)
    {
        // The library holds clips of 0, 1 and 1,002 bytes.
        Assert.Null(_reader.Read(WriteBytes(new byte[length])));
    }

    [Fact]
    public void AFileThatIsNotThereCannotSay()
    {
        // A share that is away reads the same way: not known, which is not upright.
        Assert.Null(_reader.Read(Path.Combine(_root, "gone", "clip.mp4")));
    }

    [Fact]
    public void AFileThePlayerHasOpenIsStillRead()
    {
        string clip = Write(TestMp4.FileType(), TestMp4.Movie(TestMp4.VideoTrack(180)));

        using var held = new FileStream(
            clip, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Equal(180, _reader.Read(clip)?.Degrees);
    }

    private string Write(params byte[][] boxes) => WriteBytes(TestMp4.File(boxes));

    private string WriteBytes(byte[] bytes)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, bytes);
        return path;
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
