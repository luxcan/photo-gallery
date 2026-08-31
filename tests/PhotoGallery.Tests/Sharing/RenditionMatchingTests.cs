using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Which of another machine's prepared photographs this one may take, and which
/// it must prepare itself.
/// </summary>
/// <remarks>
/// The two rules that keep the pool safe are here rather than in the copying,
/// because getting either wrong shows the wrong picture silently and the person
/// looking at it has no way to tell.
/// </remarks>
public sealed class RenditionMatchingTests
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Share = new("5ba4ed00-0000-4000-8000-000000000001");

    [Fact]
    public void APhotographWhoseBytesAgreeIsFilledInFromThePool()
    {
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("a.jpg", 1024, Monday)],
            [Set(Fact("a.jpg", 1024, Monday, "aa11"))],
            []);

        PreparedFact filled = Assert.Single(plan.FillIn);
        Assert.Equal("aa11.jpg", filled.ThumbnailName);
        Assert.Contains("aa11.jpg", plan.Wanted);
        Assert.Equal(0, plan.Mismatched);
    }

    [Fact]
    public void APhotographAtTheSamePathWhoseBytesDifferIsPreparedHere()
    {
        // The one place the change detector is load-bearing rather than
        // advisory. The path says which photograph; the bytes say which picture,
        // and a copy re-saved or re-encoded is a different file wearing the same
        // name.
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("a.jpg", 2048, Monday)],
            [Set(Fact("a.jpg", 1024, Monday, "aa11"))],
            []);

        Assert.Empty(plan.FillIn);
        Assert.Equal(1, plan.Mismatched);
    }

    [Fact]
    public void AndSoIsOneWhoseModifiedTimeDiffers()
    {
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("a.jpg", 1024, Monday.AddSeconds(1))],
            [Set(Fact("a.jpg", 1024, Monday, "aa11"))],
            []);

        Assert.Empty(plan.FillIn);
        Assert.Equal(1, plan.Mismatched);
    }

    [Fact]
    public void ARenditionThisMachineHasNotIndexedIsNotApplied()
    {
        // The pool never creates an asset. Without that rule the app would grow
        // a new state - a photograph it can show but whose original it cannot
        // reach - and every screen would have to learn about it.
        PoolPlan plan = RenditionMatching.Match(
            [],
            [Set(Fact("only-on-hers.jpg", 1024, Monday, "aa11"))],
            []);

        Assert.Empty(plan.FillIn);
        Assert.Empty(plan.Wanted);
    }

    [Fact]
    public void RunningItTwiceCopiesNothingTheSecondTime()
    {
        // What makes this idempotent: copying is "take the names I do not have".
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("a.jpg", 1024, Monday)],
            [Set(Fact("a.jpg", 1024, Monday, "aa11"))],
            ["aa11.jpg"]);

        // The row still has to be filled in - the picture being on disk is not
        // the same as the facts being on the row - but nothing is fetched.
        Assert.Single(plan.FillIn);
        Assert.Empty(plan.Wanted);
    }

    [Fact]
    public void SeveralRowsSharingOnePictureFetchItOnce()
    {
        // Four hundred sets of duplicates on this library, and identical bytes
        // share one cached picture.
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("a.jpg", 1024, Monday), Waiting(@"copies\a.jpg", 1024, Monday)],
            [
                Set(
                    Fact("a.jpg", 1024, Monday, "aa11"),
                    Fact(@"copies\a.jpg", 1024, Monday, "aa11")),
            ],
            []);

        Assert.Equal(2, plan.FillIn.Count);
        Assert.Single(plan.Wanted);
    }

    [Fact]
    public void AFileThatWillNeverDecodeCarriesItsStatusAndNoPicture()
    {
        // The twelve files on this library that fail should not be read again on
        // four more machines.
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("broken.jpg", 1024, Monday)],
            [Set(Fact("broken.jpg", 1024, Monday, null, AssetStatus.Failed))],
            []);

        PreparedFact filled = Assert.Single(plan.FillIn);
        Assert.Equal(AssetStatus.Failed, filled.Status);
        Assert.False(filled.HasPicture);
        Assert.Empty(plan.Wanted);
    }

    [Fact]
    public void AManifestCannotTurnARenditionNameIntoAPath()
    {
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("a.jpg", 1024, Monday)],
            [Set(Fact("a.jpg", 1024, Monday, @"..\..\outside"))],
            []);

        Assert.Empty(plan.FillIn);
        Assert.Empty(plan.Wanted);
        Assert.Equal(1, plan.Mismatched);
    }

    [Fact]
    public void TheLaterManifestWinsWhereTwoMachinesDisagree()
    {
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("a.jpg", 2048, Monday)],
            [
                Set(Fact("a.jpg", 1024, Monday, "old")) with { WrittenUtc = Monday },
                Set(Fact("a.jpg", 2048, Monday, "new")) with { WrittenUtc = Monday.AddHours(1) },
            ],
            []);

        PreparedFact filled = Assert.Single(plan.FillIn);
        Assert.Equal("new.jpg", filled.ThumbnailName);
    }

    // ------------------------------------------------------------- offering

    [Fact]
    public void APictureNobodyHasTurnedIsOffered()
    {
        IReadOnlyCollection<string> offer =
            RenditionMatching.Offerable([new PooledRendition("aa11.jpg", 0)], []);

        Assert.Equal(["aa11.jpg"], offer);
    }

    [Fact]
    public void ATurnedPictureIsNeverOffered()
    {
        // A turn rewrites both files in place under a name derived from the
        // original's bytes, which the turn does not change - so the same name
        // means two different pictures, and "take the names I do not have" would
        // hand somebody a sideways tile at random.
        IReadOnlyCollection<string> offer =
            RenditionMatching.Offerable([new PooledRendition("aa11.jpg", 90)], []);

        Assert.Empty(offer);
    }

    [Fact]
    public void AndNotEvenWhenAnotherRowSharesThatPictureUpright()
    {
        // Duplicates share one cached picture, so one of them being turned makes
        // that picture unfit for everybody. A single pass over the rows would
        // offer it or not depending on which duplicate came first.
        IReadOnlyCollection<string> offer = RenditionMatching.Offerable(
            [new PooledRendition("aa11.jpg", 0), new PooledRendition("aa11.jpg", 90)], []);

        Assert.Empty(offer);
    }

    [Fact]
    public void APictureThePoolAlreadyHasIsNotOfferedAgain()
    {
        IReadOnlyCollection<string> offer =
            RenditionMatching.Offerable([new PooledRendition("aa11.jpg", 0)], ["aa11.jpg"]);

        Assert.Empty(offer);
    }

    [Fact]
    public void FetchingStaysUnconditionalForAPhotographThisMachineHasTurned()
    {
        // The asymmetry, and it is deliberate: a machine that has merged a turn
        // still needs the as-generated rendition, because that is the only one
        // the pool holds, and turns it itself once it has it. Forbidding the
        // fetch too would leave exactly the photographs somebody cared enough to
        // straighten falling back to an hour of reading originals.
        PoolPlan plan = RenditionMatching.Match(
            [Waiting("a.jpg", 1024, Monday)],
            [Set(Fact("a.jpg", 1024, Monday, "aa11"))],
            []);

        Assert.Single(plan.FillIn);
        Assert.Contains("aa11.jpg", plan.Wanted);
    }

    // ------------------------------------------------------------------ setup

    private static Unprepared Waiting(string path, long length, DateTime modifiedUtc) =>
        new(new AssetKey(Share, path), length, modifiedUtc);

    private static PreparedFact Fact(
        string path,
        long length,
        DateTime modifiedUtc,
        string? name,
        AssetStatus status = AssetStatus.Ready) =>
        new(
            new AssetKey(Share, path),
            length,
            modifiedUtc,
            name,
            name is null ? null : name + ".jpg",
            1000,
            800,
            modifiedUtc,
            null,
            null,
            null,
            null,
            status,
            []);

    private static PreparedSet Set(params PreparedFact[] facts) =>
        new(
            new MachineIdentity(Guid.NewGuid(), "Mum's laptop", "1.0.0", 1),
            Monday,
            facts,
            new Dictionary<string, string>());
}
