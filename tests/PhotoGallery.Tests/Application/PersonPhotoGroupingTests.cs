using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Cutting one person's pictures into the ages they were.
/// </summary>
/// <remarks>
/// The date used is the one the reader ordered by - the capture date where the
/// file has one, the file's own date where it has not. Grouping on capture dates
/// alone was measured against the real library and rejected: three of the
/// fifteen people named there have almost none, and one has none at all, so that
/// rule would have shown those people a single "not known" heading and nothing
/// else.
/// </remarks>
public sealed class PersonPhotoGroupingTests
{
    [Fact]
    public void Group_RunsFromTheOldestAgeDownAsTheNewestPicturesLead()
    {
        // The order the reader returns, newest first, so the ages descend.
        IReadOnlyList<AgeGroup> groups = PersonPhotoGrouping.Into(
            [Taken(2020), Taken(2020), Taken(1988)], birthYear: 1983);

        Assert.Equal(["Age 37", "Age 5"], groups.Select(group => group.Heading));
        Assert.Equal([2, 1], groups.Select(group => group.Photos.Count));
    }

    [Fact]
    public void Group_WithNoYearOfBirthIsOneUnheadedRun()
    {
        // Nothing is invented from the pictures alone: without a year to measure
        // from the screen shows the same flat grid the library does.
        IReadOnlyList<AgeGroup> groups = PersonPhotoGrouping.Into(
            [Taken(2020), Taken(1988)], birthYear: null);

        AgeGroup only = Assert.Single(groups);
        Assert.Null(only.Heading);
        Assert.Null(only.Bucket);
        Assert.Equal(2, only.Photos.Count);
    }

    [Fact]
    public void Group_CountsHowManyOfEachRunWereDatedFromTheFile()
    {
        // A file date is only ever later than the shutter, so an age read off one
        // is never too young and may be too old. The count is what lets the
        // screen say so instead of presenting the age as measured.
        IReadOnlyList<AgeGroup> groups = PersonPhotoGrouping.Into(
            [Taken(2020), FromFileDate(2020), FromFileDate(2020)], birthYear: 1983);

        AgeGroup only = Assert.Single(groups);
        Assert.Equal(3, only.Photos.Count);
        Assert.Equal(2, only.DatedFromTheFile);
        Assert.False(only.IsEntirelyInferred);
    }

    [Fact]
    public void Group_OfSomebodyWithNoCaptureDatesAtAllStillGetsTheirAges()
    {
        // Noor: 89 confirmed pictures on the real library, 89 of them with no
        // capture date. Grouping on capture dates alone gave this person one
        // empty heading, which is why the file date stands in.
        IReadOnlyList<AgeGroup> groups = PersonPhotoGrouping.Into(
            [FromFileDate(2020), FromFileDate(1988)], birthYear: 1983);

        Assert.Equal(["Age 37", "Age 5"], groups.Select(group => group.Heading));
        Assert.All(groups, group => Assert.True(group.IsEntirelyInferred));
    }

    [Fact]
    public void Group_GathersEverythingDatedBeforeTheBirthInOnePlace()
    {
        // Misdated files, not people aged minus three. Left ungrouped they would
        // each become their own heading.
        IReadOnlyList<AgeGroup> groups = PersonPhotoGrouping.Into(
            [Taken(2020), FromFileDate(2012), FromFileDate(2011)], birthYear: 2015);

        Assert.Equal(2, groups.Count);
        Assert.Equal("Age 5", groups[0].Heading);
        Assert.Equal("Dated before they were born", groups[1].Heading);
        Assert.Equal(2, groups[1].Photos.Count);
    }

    [Fact]
    public void Group_KeepsTheOrderTheReaderChose()
    {
        // The reader breaks ties between rows sharing a date by id, and the walk
        // preserves it. Regrouping with a GroupBy would answer the same ages and
        // silently reshuffle the pictures inside them.
        IReadOnlyList<AgeGroup> groups = PersonPhotoGrouping.Into(
            [Taken(2020, id: 7), Taken(2020, id: 4), Taken(2020, id: 1)], birthYear: 1983);

        Assert.Equal([7, 4, 1], Assert.Single(groups).Photos.Select(photo => photo.Id));
    }

    [Fact]
    public void Group_OfNothingIsNoGroupsRatherThanOneEmptyOne() =>
        Assert.Empty(PersonPhotoGrouping.Into([], birthYear: 1983));

    [Fact]
    public void Group_TreatsAVideoLikeAnyOtherPicture()
    {
        // Every video in the library is dated from its file, because the shell
        // that gives a poster gives no capture date with it. They are still
        // pictures of the person and belong beside the rest.
        IReadOnlyList<AgeGroup> groups = PersonPhotoGrouping.Into(
            [FromFileDate(2020, kind: AssetKind.Video)], birthYear: 1983);

        Assert.Equal("Age 37", Assert.Single(groups).Heading);
    }

    private static GalleryItem Taken(int year, int id = 1) => Item(year, dated: true, id, AssetKind.Photo);

    private static GalleryItem FromFileDate(int year, int id = 1, AssetKind kind = AssetKind.Photo) =>
        Item(year, dated: false, id, kind);

    private static GalleryItem Item(int year, bool dated, int id, AssetKind kind)
    {
        var when = new DateTime(year, 6, 3, 12, 0, 0, DateTimeKind.Utc);
        return new GalleryItem(
            id,
            $@"{year}\{id}.jpg",
            $"{id}.jpg",
            $@"{year}",
            $@"C:\one\{year}\{id}.jpg",
            $"{id}",
            dated ? when : null,
            when,
            0,
            kind);
    }
}
