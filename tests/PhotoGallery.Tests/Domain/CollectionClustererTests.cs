using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// Grouping photographs into occasions.
/// </summary>
/// <remarks>
/// The first version of this feature was specified with a single threshold -
/// start a new collection after a six-hour gap - and simulating it over 9,544
/// real photographs showed that not one collection would ever span more than a
/// day, because everybody sleeps. A three-day trip came out as three separate
/// days, which is the one thing the feature exists to avoid.
///
/// <para>So the rules here are two-level, and these tests are mostly about the
/// second level: a night does not end an occasion, a quiet day does, and a run
/// that never ends is ordinary life rather than a holiday.</para>
/// </remarks>
public sealed class CollectionClustererTests
{
    private static readonly DateTime Noon = new(2019, 3, 3, 12, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void ANightsSleepDoesNotEndATrip()
    {
        // Three days away, each with an afternoon and an evening. The whole
        // point: one collection, not three.
        List<DatedPhoto> photos =
        [
            .. Day(Noon, count: 6),
            .. Day(Noon.AddDays(1), count: 6),
            .. Day(Noon.AddDays(2), count: 6),
        ];

        PhotoGroup group = Assert.Single(CollectionClusterer.Group(photos));

        Assert.Equal(18, group.AssetIds.Count);
        Assert.Equal(3, group.Days);
        Assert.Equal("2019-03-03..2019-03-05", group.Key);
    }

    [Fact]
    public void ADayWithNoPhotographsEndsTheRun()
    {
        List<DatedPhoto> photos =
        [
            .. Day(Noon, count: 10),
            .. Day(Noon.AddDays(2), count: 10),
        ];

        IReadOnlyList<PhotoGroup> groups = CollectionClusterer.Group(photos);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal(1, group.Days));
    }

    [Fact]
    public void ARunLongerThanTheCapIsOfferedAsItsDaysInstead()
    {
        // A month of photographs every single day is not a month-long holiday.
        // Three weeks still counts as one occasion - see the test below - and
        // the line sits there because measuring the alternatives put it there.
        List<DatedPhoto> photos = [.. Enumerable
            .Range(0, 30)
            .SelectMany(day => Day(Noon.AddDays(day), count: 9))];

        IReadOnlyList<PhotoGroup> groups = CollectionClusterer.Group(photos);

        Assert.Equal(30, groups.Count);
        Assert.All(groups, group => Assert.Equal(1, group.Days));
        Assert.All(groups, group => Assert.Equal(CollectionKind.Period, group.Kind));
    }

    [Fact]
    public void AnOccasionRightUpToTheCapIsStillOneOccasion()
    {
        List<DatedPhoto> photos = [.. Enumerable
            .Range(0, CollectionClusterer.LongestRunDays)
            .SelectMany(day => Day(Noon.AddDays(day), count: 9))];

        PhotoGroup group = Assert.Single(CollectionClusterer.Group(photos));

        Assert.Equal(CollectionClusterer.LongestRunDays, group.Days);
    }

    [Theory]
    [InlineData(7, 240, false)]   // too few photographs: a handful of shots
    [InlineData(8, 80, false)]    // too short: a single burst
    [InlineData(8, 240, true)]    // both earned
    public void AGroupHasToEarnItsPlace(int count, int minutesApart, bool expected)
    {
        List<DatedPhoto> photos = [.. Enumerable.Range(0, count).Select(i => new DatedPhoto(
            i + 1,
            Noon.AddMinutes(i * (minutesApart / (double)Math.Max(count - 1, 1))),
            null,
            null))];

        Assert.Equal(expected, CollectionClusterer.Group(photos).Count == 1);
    }

    [Fact]
    public void PhotographsHoursApartOnOneDayAreOneSessionNotTwo()
    {
        // A morning at the beach and an evening out is one day, because the gap
        // between them is under six hours.
        List<DatedPhoto> photos =
        [
            .. Day(Noon, count: 5),
            .. Day(Noon.AddHours(5), count: 5),
        ];

        PhotoGroup group = Assert.Single(CollectionClusterer.Group(photos));

        Assert.Equal(10, group.AssetIds.Count);
    }

    [Fact]
    public void ADayAtHomeIsNotCalledATrip()
    {
        List<DatedPhoto> photos = [.. AtHome(40)];

        Assert.All(
            CollectionClusterer.Group(photos),
            group => Assert.NotEqual(CollectionKind.Trip, group.Kind));
    }

    [Fact]
    public void AWeekendFarFromHomeIsCalledATrip()
    {
        // Home is where the bulk of the library is; the weekend is 400 km away,
        // and well clear of the days the home photographs sit on - a photograph
        // taken at home on the morning you leave belongs to the trip's run, and
        // that is correct rather than a fault.
        List<DatedPhoto> photos =
        [
            .. AtHome(40),
            .. Day(Noon.AddDays(100), count: 6, latitude: 4.6d, longitude: 101.1d),
            .. Day(Noon.AddDays(101), count: 6, latitude: 4.6d, longitude: 101.1d),
        ];

        PhotoGroup trip = Assert.Single(
            CollectionClusterer.Group(photos), group => group.Kind == CollectionKind.Trip);

        Assert.Equal(12, trip.AssetIds.Count);
        Assert.Equal(2, trip.Days);
    }

    [Fact]
    public void ALibraryWithTooFewCoordinatesCallsNothingATrip()
    {
        // Three coordinates do not establish where somebody lives, and calling
        // their midpoint home would make a trip of everything else.
        List<DatedPhoto> photos =
        [
            .. Day(Noon, count: 9, latitude: 1.29d, longitude: 103.85d),
            .. Day(Noon.AddDays(5), count: 9, latitude: 35.68d, longitude: 139.69d),
        ];

        Assert.All(
            CollectionClusterer.Group(photos),
            group => Assert.NotEqual(CollectionKind.Trip, group.Kind));
    }

    [Fact]
    public void NothingInMeansNothingOut()
    {
        Assert.Empty(CollectionClusterer.Group([]));
    }

    /// <summary>Enough photographs at one place to establish where home is.</summary>
    private static IEnumerable<DatedPhoto> AtHome(int days) =>
        Enumerable.Range(0, days).SelectMany(day =>
            Day(Noon.AddDays(day * 2), count: 1, latitude: 1.29d, longitude: 103.85d));

    /// <summary>An afternoon's photographs, ten minutes apart.</summary>
    private static IEnumerable<DatedPhoto> Day(
        DateTime start, int count, double? latitude = null, double? longitude = null) =>
        Enumerable.Range(0, count).Select(i => new DatedPhoto(
            NextId(), start.AddMinutes(i * 30), latitude, longitude));

    private static int s_nextId;

    private static int NextId() => ++s_nextId;
}
