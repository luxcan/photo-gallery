using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The dates the detail panel shows, and which one it admits to filing under.
/// </summary>
/// <remarks>
/// Measured on the real library: of the 1,536 photographs carrying no capture
/// date, 1,480 - 96% - have a creation date LATER than their modified date,
/// because copying stamps creation with the day of the copy and leaves the
/// modified date alone. Using the creation date collapses those 1,536 onto 32
/// distinct days; the earlier of the two spreads them over 319. That is why the
/// panel shows both and says which one won.
/// </remarks>
public sealed class PhotoDateFactsTests
{
    private static readonly DateTime Shutter = new(2014, 3, 11, 14, 22, 0, DateTimeKind.Utc);
    private static readonly DateTime CopiedOn = new(2018, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(512L, "512 bytes")]
    [InlineData(80_088L, "78 KB (80,088 bytes)")]
    [InlineData(4_470_593L, "4.3 MB (4,470,593 bytes)")]
    [InlineData(3_221_225_472L, "3.00 GB (3,221,225,472 bytes)")]
    public void Size_ReadsInTheLargestUnitAndStillCompares(long bytes, string expected)
    {
        // Both halves earn their place: bytes alone are unreadable at this
        // scale, and the rounded figure alone cannot separate two nearly
        // identical copies on the duplicate screen.
        Assert.Equal(expected, Sized(bytes).ExactSize);
    }

    [Fact]
    public void Panel_ShowsBothFileDates()
    {
        PhotoDetails details = Facts(taken: null, created: CopiedOn, modified: Shutter);

        Assert.True(details.HasFileDates);
        Assert.Contains("2018", details.Created, StringComparison.Ordinal);
        Assert.Contains("2014", details.Modified, StringComparison.Ordinal);
    }

    [Fact]
    public void Panel_SaysNothingAboutFilingWhenThereIsACaptureDate()
    {
        // The capture date is already on the panel under "Taken", so saying the
        // picture is filed under it repeats what the reader can see - and it
        // said so on almost every picture, which is how a line stops being read.
        PhotoDetails details = Facts(taken: Shutter, created: CopiedOn, modified: CopiedOn);

        Assert.Null(details.FiledUnder);
    }

    [Fact]
    public void Panel_SaysItFilesACopiedPictureUnderTheModifiedDate()
    {
        // The common case here: created is the day of the copy and later, so the
        // earlier of the two is the modified date - the one closest to the
        // shutter.
        PhotoDetails details = Facts(taken: null, created: CopiedOn, modified: Shutter);

        Assert.Contains("modified date", details.FiledUnder, StringComparison.Ordinal);
    }

    [Fact]
    public void Panel_SaysItFilesUnderTheCreatedDateWhenThatIsTheEarlierOne()
    {
        // A file that was never bulk-copied: creation is near the shutter and the
        // modified date has drifted later.
        PhotoDetails details = Facts(taken: null, created: Shutter, modified: CopiedOn);

        Assert.Contains("created date", details.FiledUnder, StringComparison.Ordinal);
    }

    [Fact]
    public void Panel_LeavesTheFileDatesOutWhenItWasNotToldThem()
    {
        // The duplicate comparison builds its panel from a query that never asked
        // for a creation date, and a labelled blank reads as a failure.
        PhotoDetails details = Facts(taken: Shutter, created: default, modified: default);

        Assert.False(details.HasFileDates);
        Assert.Equal("not recorded", details.Created);
    }

    [Fact]
    public void Panel_SaysWhenThereIsNoDateOfAnyKind()
    {
        PhotoDetails details = Facts(taken: null, created: default, modified: default);

        Assert.Contains("No date at all", details.FiledUnder, StringComparison.Ordinal);
    }

    private static PhotoDetails Sized(long bytes) =>
        PhotoDetails.Of(new PhotoFacts(
            AssetId: 1,
            FileName: "IMG_4359.JPEG",
            FolderPath: "Quotes",
            FullPath: @"C:\one\Quotes\IMG_4359.JPEG",
            Length: bytes,
            Width: 763,
            Height: 859,
            TakenUtc: null,
            ModifiedUtc: Shutter,
            ContentHash: "7604ea13"));

    private static PhotoDetails Facts(DateTime? taken, DateTime created, DateTime modified) =>
        PhotoDetails.Of(new PhotoFacts(
            AssetId: 1,
            FileName: "PL1A9921.jpg",
            FolderPath: "2015",
            FullPath: @"C:\one\2015\PL1A9921.jpg",
            Length: 4_470_593,
            Width: 6000,
            Height: 4000,
            TakenUtc: taken,
            ModifiedUtc: modified,
            ContentHash: "a3f1c2d4",
            PlaceName: null,
            CreatedUtc: created));
}
