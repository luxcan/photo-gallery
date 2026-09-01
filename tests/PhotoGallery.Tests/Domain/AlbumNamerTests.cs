using System.Globalization;
using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// Naming an occasion from what is known about it.
/// </summary>
/// <remarks>
/// The ladder exists because most rungs are unavailable most of the time. On
/// the library this was measured against, coordinates are on 11% of
/// photographs, so the bottom rung - a month and a count - is not a fallback
/// for odd cases but the normal outcome, and has to read well on its own.
/// </remarks>
public sealed class AlbumNamerTests
{
    [Fact]
    public void APlaceAndAMeasuredDistanceFromHomeIsATrip()
    {
        Assert.Equal("Genting Trip", Name(AlbumKind.Trip, places: ["Genting"]));
    }

    [Fact]
    public void ThatSamePlaceCloseToHomeIsNotCalledATrip()
    {
        Assert.Equal("Genting", Name(AlbumKind.Day, places: ["Genting"]));
    }

    [Fact]
    public void TwoPlacesAreBothNamed()
    {
        Assert.Equal(
            "Genting and Kuala Lumpur Trip",
            Name(AlbumKind.Trip, places: ["Genting", "Kuala Lumpur"]));
    }

    [Fact]
    public void ATitleIsNotAList()
    {
        Assert.Equal(
            "Genting, Kuala Lumpur and others",
            Name(AlbumKind.Event, places: ["Genting", "Kuala Lumpur", "Ipoh", "Penang"]));
    }

    [Fact]
    public void WithNoPlaceItIsNamedAfterWhoIsInIt()
    {
        // 3-5 March 2019 is a Sunday to a Tuesday, so it is not a weekend.
        Assert.Equal("3 days with Ana Lim", Name(AlbumKind.Event, people: ["Ana Lim"]));
    }

    [Fact]
    public void AWeekendIsOnlyCalledOneWhenItReallyIsOne()
    {
        // 9-10 March 2019 is a Saturday and a Sunday.
        var facts = new AlbumFacts(
            AlbumKind.Event,
            new DateTime(2019, 3, 9, 10, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2019, 3, 10, 18, 0, 0, DateTimeKind.Unspecified),
            [],
            ["Ana Lim"],
            PhotoCount: 42);

        Assert.Equal("A weekend with Ana Lim", AlbumNamer.Name(facts));
    }

    [Fact]
    public void OneDayWithSomebodyIsADay()
    {
        var facts = new AlbumFacts(
            AlbumKind.Day,
            new DateTime(2019, 3, 3, 10, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2019, 3, 3, 18, 0, 0, DateTimeKind.Unspecified),
            [],
            ["Ana Lim"],
            PhotoCount: 12);

        Assert.Equal("A day with Ana Lim", AlbumNamer.Name(facts));
    }

    [Fact]
    public void WithNeitherPlaceNorPeopleItIsTheMonth()
    {
        // The normal case on a real library, not an edge case.
        // Which days, not only which month. A fortnight of daily photographs
        // that the cap breaks apart would otherwise come back as eleven
        // albums all called "September 2019".
        Assert.Equal("3-5 March 2019", Name(AlbumKind.Period));
    }

    [Fact]
    public void TheMonthComesFromTheWallClockValueTheCameraWrote()
    {
        // A capture time carries no offset. Converting it would shift the name
        // by whatever the machine's timezone happens to be - which no test on a
        // UTC build server would ever catch.
        // Late on the last evening of the year: converted by even an hour, this
        // photograph's album would be named after New Year's Day.
        var newYearsEve = new DateTime(2019, 12, 31, 22, 30, 0, DateTimeKind.Unspecified);
        var facts = new AlbumFacts(
            AlbumKind.Period, newYearsEve, newYearsEve.AddMinutes(60), [], [], 9);

        Assert.Equal("31 December 2019", AlbumNamer.Name(facts));
    }

    private static string Name(
        AlbumKind kind,
        IReadOnlyList<string>? places = null,
        IReadOnlyList<string>? people = null) =>
        AlbumNamer.Name(new AlbumFacts(
            kind,
            new DateTime(2019, 3, 3, 12, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2019, 3, 5, 18, 0, 0, DateTimeKind.Unspecified),
            places ?? [],
            people ?? [],
            PhotoCount: 42));
}
