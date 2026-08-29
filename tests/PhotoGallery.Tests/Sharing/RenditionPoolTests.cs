using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Two libraries, a folder between them, and the cached pictures actually
/// crossing it.
/// </summary>
/// <remarks>
/// The claim worth building for: a new machine made complete from about 2.1 GB
/// without opening a single original - roughly four hours of reading and
/// computing replaced by five minutes of copying.
/// </remarks>
public sealed class RenditionPoolTests : IDisposable
{
    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task APhotographPreparedOnOneMachineIsFilledInOnTheOther()
    {
        // The whole of the second half: Dad's crawl found the file, and he never
        // opens it.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Photo(@"2019\a.jpg");

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.True(taken.Ran);
        Assert.Equal(1, taken.Filled);
        Assert.Equal(1, taken.Fetched);
        Assert.True(Dad.Holds("aa11.jpg"));

        Asset filled = Dad.Db.Assets.AsNoTracking().Single();
        Assert.Equal(AssetStatus.Ready, filled.Status);
        Assert.Equal("aa11.jpg", filled.ThumbnailName);
    }

    [Fact]
    public async Task ItCarriesTheFactsAndNotJustThePicture()
    {
        // A machine that copied the pictures but not what the decode learned
        // would get a library with no timeline, no places and nothing to cluster
        // occasions from.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Photo(@"2019\a.jpg");

        await Mum.Pooling.HandleAsync();
        await Dad.Pooling.HandleAsync();

        Asset filled = Dad.Db.Assets.AsNoTracking().Single();
        Assert.Equal(new DateTime(2019, 7, 4, 10, 0, 0, DateTimeKind.Utc), filled.TakenUtc);
        Assert.Equal(1.29, filled.Latitude);
        Assert.Equal(103.85, filled.Longitude);
        Assert.Equal(1000, filled.Width);
        Assert.Equal(800, filled.Height);
        Assert.Equal("aa11", filled.ContentHash);
    }

    [Fact]
    public async Task AfterwardsThereIsNothingLeftToPrepare()
    {
        // The generating phase finds nothing outstanding, which is the hour
        // actually gone rather than merely deferred.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Photo(@"2019\a.jpg");

        await Mum.Pooling.HandleAsync();
        await Dad.Pooling.HandleAsync();

        Assert.Empty(await Dad.Decisions.UnpreparedAsync());
    }

    [Fact]
    public async Task NoOriginalIsEverSentRequestedOrReceived()
    {
        // Asserted against the folder itself: whatever else is in there, it is
        // the two renditions this app made and a manifest.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.HandleAsync();

        string[] everything = Directory.GetFiles(
            _house.SharedFolder, "*", SearchOption.AllDirectories);

        Assert.All(everything, path => Assert.True(
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(SharedFolderPool.Extension, StringComparison.OrdinalIgnoreCase),
            $"something other than a rendition or a manifest reached the pool: {path}"));

        Assert.Equal(2, everything.Count(p => p.EndsWith(".jpg", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RunningItTwiceCopiesNothingTheSecondTime()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Photo(@"2019\a.jpg");

        await Mum.Pooling.HandleAsync();
        await Dad.Pooling.HandleAsync();

        PoolResult again = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, again.Fetched);
        Assert.Equal(0, again.Filled);
        Assert.True(again.ChangedNothing);
    }

    [Fact]
    public async Task APhotographWhoseBytesChangedIsPreparedHereInstead()
    {
        // Silently showing the wrong picture is the one failure this cannot
        // afford, so the file has to agree byte for byte and to the second.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");

        Asset mine = Dad.Photo(@"2019\a.jpg");
        mine.Length += 1;
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, taken.Filled);
        Assert.Equal(1, taken.Mismatched);
        Assert.Contains("prepared here instead", taken.Summary);

        // Nothing was written onto the row, so the preparing pass still owns it.
        Assert.Null(Dad.Db.Assets.AsNoTracking().Single().ThumbnailName);
    }

    [Fact]
    public async Task ARenditionWhoseRowThisMachineHasNotIndexedCreatesNoAsset()
    {
        // The pool fills in rows; it does not make them. Otherwise the app grows
        // a photograph it can show but whose original it cannot reach, and every
        // screen has to learn about it.
        Mum.Prepared(@"2026 Phone Dump\b.jpg", "bb22.jpg");

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, taken.Filled);
        Assert.Empty(Dad.Db.Assets);
    }

    [Fact]
    public async Task ATurnedPhotographIsNeverPooled()
    {
        // The turn rewrote both files in place under a name derived from the
        // original's bytes, which the turn did not change. Offering it would put
        // a sideways tile under a name that means upright everywhere else.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg", rotation: 90);
        Dad.Photo(@"2019\a.jpg");

        PoolResult offered = await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, offered.Offered);
        Assert.Equal(0, taken.Filled);
        Assert.False(Dad.Holds("aa11.jpg"));
    }

    [Fact]
    public async Task AFileThatWillNeverDecodeIsNotReadAgainOnTheNextMachine()
    {
        Asset broken = Mum.Photo(@"2019\broken.jpg");
        broken.Status = AssetStatus.Failed;
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();

        Dad.Photo(@"2019\broken.jpg");

        await Mum.Pooling.HandleAsync();
        await Dad.Pooling.HandleAsync();

        Assert.Equal(AssetStatus.Failed, Dad.Db.Assets.AsNoTracking().Single().Status);

        // And taking it again is not work. The row still has no picture, so the
        // pool keeps offering the news in case another machine ever succeeds -
        // but landing the same answer twice changes nothing and is not counted
        // as though it had.
        PoolResult again = await Dad.Pooling.HandleAsync();
        Assert.Equal(0, again.Filled);
    }

    [Fact]
    public async Task ThePreviewIsWrittenBeforeTheTile()
    {
        // IThumbnailStore.Exists asks only about the tile, so a copy interrupted
        // between the two would leave a photograph reporting itself complete
        // with no preview - which is the file the viewer opens and the face
        // detector reads.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.HandleAsync();

        string tile = Path.Combine(
            _house.SharedFolder, SharedFolderPool.ThumbsFolder, "aa", "aa11.jpg");
        string preview = Path.Combine(
            _house.SharedFolder, SharedFolderPool.ThumbsFolder, "aa", "aa11-p.jpg");

        Assert.True(File.Exists(tile));
        Assert.True(File.Exists(preview));
        Assert.True(
            File.GetLastWriteTimeUtc(preview) <= File.GetLastWriteTimeUtc(tile),
            "the tile was written before the preview");
    }

    [Fact]
    public async Task AHalfCopiedPairIsNotOfferedAsAvailable()
    {
        // Two machines will fetch the same missing rendition at the same moment,
        // and a third must never read half a JPEG. A name whose preview has not
        // landed is a name the pool does not admit to having.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.HandleAsync();

        File.Delete(Path.Combine(
            _house.SharedFolder, SharedFolderPool.ThumbsFolder, "aa", "aa11-p.jpg"));

        Assert.Empty(await Dad.Pool.NamesAsync());
    }

    [Fact]
    public async Task WithNoFolderChosenItSaysSoRatherThanFailing()
    {
        using var alone = new TwoLibraries();

        PoolResult taken = await alone.Mum.Pooling.HandleAsync();

        Assert.False(taken.Ran);
        Assert.Contains("Choose a folder", taken.Summary);
    }

    [Fact]
    public async Task AMachineDoesNotTakeItsOwnPicturesBack()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");

        PoolResult first = await Mum.Pooling.HandleAsync();
        PoolResult second = await Mum.Pooling.HandleAsync();

        Assert.Equal(1, first.Offered);
        Assert.Equal(0, second.Offered);
        Assert.Equal(0, second.Filled);
    }

    public void Dispose() => _house.Dispose();
}
