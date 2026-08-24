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
