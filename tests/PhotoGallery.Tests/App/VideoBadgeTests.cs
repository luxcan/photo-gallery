using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What a video cell says about itself.
/// </summary>
/// <remarks>
/// Videos only reached the grid once they had a poster, so the badge is the
/// whole of what tells somebody a cell is a film rather than a photograph. It
/// has to keep saying that when the length is not known, which on the shell
/// decoder is every clip.
/// </remarks>
public sealed class VideoBadgeTests
{
    [Fact]
    public void AClipUnderAnHourIsNotPaddedWithHours()
    {
        GalleryTile tile = Video(TimeSpan.FromSeconds(95));

        Assert.Equal("1:35", tile.DurationCaption);
        Assert.True(tile.HasDuration);
    }

    [Fact]
    public void ASecondsLongClipStillReadsAsMinutesAndSeconds()
    {
        Assert.Equal("0:30", Video(TimeSpan.FromSeconds(30)).DurationCaption);
    }

    [Fact]
    public void AnHourLongClipGrowsTheHoursField()
    {
        Assert.Equal(
            "1:05:04",
            Video(new TimeSpan(1, 5, 4)).DurationCaption);
    }

    [Fact]
    public void AClipWhoseLengthWasNeverLearntShowsTheGlyphAlone()
    {
        // The shell hands back a picture and does not say how long the film is,
        // so this is not a corner - it is every video the app has today.
        GalleryTile tile = Video(duration: null);

        Assert.Equal(string.Empty, tile.DurationCaption);
        Assert.False(tile.HasDuration);
        Assert.True(tile.IsVideo);
    }

    [Fact]
    public void APhotographIsNotAVideoAndSaysNoLength()
    {
        var tile = new GalleryTile(Item(AssetKind.Photo, duration: null));

        Assert.False(tile.IsVideo);
        Assert.False(tile.HasDuration);
    }

    private static GalleryTile Video(TimeSpan? duration) =>
        new(Item(AssetKind.Video, duration));

    private static GalleryItem Item(AssetKind kind, TimeSpan? duration) =>
        new(1,
            @"2023\clip.mov",
            "clip.mov",
            "2023",
            @"C:\videos\2023\clip.mov",
            "ab/abcdef.jpg",
            null,
            new DateTime(2023, 3, 5, 0, 0, 0, DateTimeKind.Utc),
            0,
            kind,
            duration);
}
