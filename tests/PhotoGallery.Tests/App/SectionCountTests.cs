using System.Globalization;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The number beside a section's name in the open nav. Each section counts a
/// different thing, and a section that counts nothing has to say nothing rather
/// than nought - a library with no photographs in it would otherwise open onto a
/// column of zeroes.
/// </summary>
public sealed class SectionCountTests
{
    private readonly SectionCountConverter _converter = new();

    private static readonly LibraryCounts Counts = new(
        Photos: 1200,
        Videos: 34,
        VideosPrepared: 30,
        VideosUnreadable: 0,
        Thumbnails: 1200,
        Faces: 800,
        People: 15,
        UnresolvedDuplicateSets: 7);

    [Fact]
    public void EachSectionCountsItsOwnThing()
    {
        string thousands = CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator;

        Assert.Equal($"1{thousands}234", Count(ActivitySection.LibraryKey));
        Assert.Equal("15", Count(ActivitySection.PeopleKey));
        Assert.Equal("7", Count(ActivitySection.DuplicatesKey));
        Assert.Equal("2", Count(ActivitySection.SourcesKey));
    }

    [Fact]
    public void SectionsThatCountNothing_SayNothing()
    {
        Assert.Equal(string.Empty, Count(ActivitySection.AboutKey));
        Assert.Equal(string.Empty, Count(ActivitySection.SettingsKey));
    }

    [Fact]
    public void ANoughtIsNotWorthSaying()
    {
        Assert.Equal(
            string.Empty,
            _converter.Convert(
                [ActivitySection.LibraryKey, LibraryCounts.Empty, 0],
                typeof(string),
                null,
                CultureInfo.InvariantCulture));
    }

    private object Count(string key) =>
        _converter.Convert(
            [key, Counts, 2],
            typeof(string),
            null,
            CultureInfo.InvariantCulture);
}
