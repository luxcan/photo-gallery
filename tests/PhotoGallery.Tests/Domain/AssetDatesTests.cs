using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// Which date a photograph is filed under when it does not carry its own.
/// </summary>
/// <remarks>
/// Measured on a real library of 16,225 files, against the 9,882 that do carry a
/// capture date: the modified date alone was right 7,309 times, the creation
/// date alone 308, and the earlier of the two 7,527.
/// </remarks>
public sealed class AssetDatesTests
{
    private static readonly DateTime Taken = new(2014, 3, 11, 14, 22, 7, DateTimeKind.Utc);
    private static readonly DateTime Early = new(2014, 3, 11, 14, 22, 9, DateTimeKind.Utc);
    private static readonly DateTime Late = new(2026, 4, 26, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BestGuess_PrefersThePhotographsOwnDateOverAnyFileDate() =>
        Assert.Equal(Taken, AssetDates.BestGuess(Taken, Early, Late));

    [Fact]
    public void BestGuess_TakesTheEarlierFileDateWhenCreationSurvived()
    {
        // What a file that was never bulk-copied looks like: its creation date is
        // close to the shutter and its modified date has drifted.
        Assert.Equal(Early, AssetDates.BestGuess(null, Early, Late));
    }

    [Fact]
    public void BestGuess_TakesTheModifiedDateWhenCopyingResetTheCreationDate()
    {
        // The common case, and the one that makes creation alone useless: copying
        // preserves the modified date and stamps creation with the day of the
        // copy. 2,758 files in the measured library share one such day.
        Assert.Equal(Early, AssetDates.BestGuess(null, Late, Early));
    }

    [Fact]
    public void BestGuess_IgnoresAnUnknownCreationDate()
    {
        // Rows indexed before creation dates were recorded hold the sentinel.
        // Treating that as "earlier" would date the whole library to year one.
        Assert.Equal(Early, AssetDates.BestGuess(null, default, Early));
    }

    /// <summary>
    /// The rule written for the database says the same as the rule written for
    /// memory, over every shape of input there is.
    /// </summary>
    /// <remarks>
    /// A method group cannot be translated to SQL, so the rule exists twice.
    /// This is the test the second copy's doc comment promises: it compiles the
    /// expression and holds it against the method for every combination of a
    /// capture date, a surviving creation date, a reset one and the sentinel.
    /// </remarks>
    [Fact]
    public void TheQueryableRuleAgreesWithTheOneInMemory()
    {
        Func<Asset, DateTime> queryable = AssetDates.Taken.Compile();

        foreach (DateTime? taken in new DateTime?[] { Taken, null })
        {
            foreach (DateTime created in new[] { Early, Late, default })
            {
                foreach (DateTime modified in new[] { Early, Late })
                {
                    Assert.Equal(
                        AssetDates.BestGuess(taken, created, modified),
                        queryable(Photo(taken, created, modified)));
                }
            }
        }
    }

    /// <summary>
    /// A file carrying no capture date is still inside a day it falls on.
    /// </summary>
    /// <remarks>
    /// The whole point of the range reading the fallback: every video in a real
    /// library is this file, so the stricter reading meant a day rule could
    /// never match a video while every screen showed it sitting on that day.
    /// </remarks>
    [Fact]
    public void TakenBetween_ReachesAFileWithNoCaptureDateOfItsOwn()
    {
        Func<Asset, bool> thatDay = AssetDates
            .TakenBetween(DateOnly.FromDateTime(Early), DateOnly.FromDateTime(Early))
            .Compile();

        Assert.True(thatDay(Photo(null, Late, Early)));
        Assert.False(thatDay(Photo(null, Late, Late)));
    }

    /// <summary>One day means that day, not the instant it begins.</summary>
    [Fact]
    public void TakenBetween_HoldsTheLastDayWhole()
    {
        Func<Asset, bool> thatDay = AssetDates
            .TakenBetween(new DateOnly(2014, 3, 11), new DateOnly(2014, 3, 11))
            .Compile();

        Assert.True(thatDay(Photo(new DateTime(2014, 3, 11, 23, 59, 59), default, Late)));
        Assert.False(thatDay(Photo(new DateTime(2014, 3, 12, 0, 0, 0), default, Late)));
    }

    /// <summary>An end left open is no limit at that end.</summary>
    [Fact]
    public void TakenBetween_WithAnOpenEndIsBoundedOnlyAtTheOther()
    {
        Func<Asset, bool> since =
            AssetDates.TakenBetween(new DateOnly(2020, 1, 1), null).Compile();

        Assert.True(since(Photo(Late, default, Late)));
        Assert.False(since(Photo(Taken, default, Late)));

        Func<Asset, bool> anything = AssetDates.TakenBetween(null, null).Compile();

        Assert.True(anything(Photo(Taken, default, Late)));
        Assert.True(anything(Photo(null, default, Late)));
    }

    /// <summary>A photograph carrying nothing but its three dates.</summary>
    private static Asset Photo(DateTime? taken, DateTime created, DateTime modified) =>
        new()
        {
            RelativePath = "a.jpg",
            TakenUtc = taken,
            CreatedUtc = created,
            ModifiedUtc = modified,
        };

    [Fact]
    public void BestGuess_IsNeverLaterThanTheModifiedDate()
    {
        // The property the rule rests on: a file's timestamps only ever move
        // forward from the moment the shutter fired, so the earlier of them is
        // never a worse guess than the modified date on its own.
        foreach (DateTime created in new[] { Early, Late, default })
        {
            Assert.True(AssetDates.BestGuess(null, created, Late) <= Late);
        }
    }
}
