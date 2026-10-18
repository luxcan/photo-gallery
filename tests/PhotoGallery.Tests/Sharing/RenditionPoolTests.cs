using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Sharing;
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

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

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

        await Mum.Pooling.GiveAsync();
        await Dad.Pooling.TakeAsync();

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

        await Mum.Pooling.GiveAsync();
        await Dad.Pooling.TakeAsync();

        Assert.Empty(await Dad.Decisions.UnpreparedAsync());
    }

    [Fact]
    public async Task NoOriginalIsEverSentRequestedOrReceived()
    {
        // Asserted against the folder itself: whatever else is in there, it is
        // the two renditions this app made and a manifest.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.GiveAsync();

        string[] everything = Directory.GetFiles(
            _house.SharedFolder, "*", SearchOption.AllDirectories);

        // Renditions this app made, and its own gzipped payloads. Anything else
        // in here would be a file that came out of somebody's camera.
        Assert.All(everything, path => Assert.True(
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase),
            $"something other than a rendition or a payload reached the pool: {path}"));

        Assert.Equal(2, everything.Count(p => p.EndsWith(".jpg", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RunningItTwiceCopiesNothingTheSecondTime()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Photo(@"2019\a.jpg");

        await Mum.Pooling.GiveAsync();
        await Dad.Pooling.TakeAsync();

        PoolResult again = await Dad.Pooling.TakeAsync();

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

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

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

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

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

        PoolResult offered = await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, offered.Offered);
        Assert.Equal(0, taken.Filled);
        Assert.False(Dad.Holds("aa11.jpg"));
    }

    /// <summary>
    /// A file one machine could not decode is still tried on the next.
    /// </summary>
    /// <remarks>
    /// Taken before the next machine made anything, "this never decoded" was
    /// the next machine's answer too, for good: its generating phase skips a
    /// failed row, and nothing tries it again until the file itself changes. A
    /// file that will not decode on one laptop is very often one that laptop
    /// has no codec for, so the news is not given and not taken.
    /// </remarks>
    [Fact]
    public async Task AFileOneMachineCouldNotDecodeIsStillTriedOnTheNext()
    {
        Asset broken = Mum.Photo(@"2019\broken.jpg");
        broken.Status = AssetStatus.Failed;
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();

        Asset his = Dad.Photo(@"2019\broken.jpg");
        his.Status = AssetStatus.Pending;
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, taken.Filled);
        Assert.Equal(AssetStatus.Pending, Dad.Db.Assets.AsNoTracking().Single().Status);
        Assert.Empty(Assert.Single(await Dad.Pool.FetchAsync()).Facts);
    }

    /// <summary>
    /// A photograph that is not ready here is not given, even while its row
    /// still names the picture it had before.
    /// </summary>
    /// <remarks>
    /// A scan that cannot reach the originals marks every photograph whose small
    /// copy has gone missing as "could not be read", and leaves the name of the
    /// copy on the row. Given, that verdict travelled with the name: it was the
    /// newest fact in the folder, so it beat what another computer had said
    /// about the same photograph, and every laptop that took it wrote "could
    /// not be read" onto a row whose picture it had just copied.
    /// </remarks>
    [Theory]
    [InlineData(AssetStatus.Failed)]
    [InlineData(AssetStatus.Skipped)]
    [InlineData(AssetStatus.Pending)]
    public async Task APhotographNotReadyHereIsNotGivenWhileItsRowStillNamesAPicture(
        AssetStatus status)
    {
        Asset mine = Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Mum.Db.Assets
            .Where(row => row.Id == mine.Id)
            .ExecuteUpdate(setters => setters.SetProperty(row => row.Status, status));

        await Mum.Pooling.GiveAsync();

        Assert.Empty(Assert.Single(await Dad.Pool.FetchAsync()).Facts);
    }

    /// <summary>
    /// A "could not be read" the build before gave, with a picture's name on it,
    /// neither lands here nor hides another computer's picture.
    /// </summary>
    /// <remarks>
    /// That build wrote every row that named a picture, whatever had become of
    /// it, and its file stays in the folder until that laptop next scans. It was
    /// also the newest file there, which is how one laptop's lost copies became
    /// every laptop's "could not be read".
    /// </remarks>
    [Fact]
    public async Task AVerdictTheBuildBeforeGaveDoesNotHideAnotherComputersPicture()
    {
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Dad.Pooling.GiveAsync();

        Asset hers = Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        MachineIdentity mum =
            await PublishDecisionsHandler.ThisMachineAsync(Mum.Index, CancellationToken.None);

        await Mum.Pool.PublishAsync(new PreparedSet(
            mum,
            DateTime.UtcNow.AddMinutes(1),
            [Failed(Mum.KeyOf(hers), hers, "aa11.jpg")],
            new Dictionary<string, string>()));

        Library gran = _house.Add("Gran's laptop");
        Asset theirs = gran.Photo(@"2019\a.jpg");
        theirs.Status = AssetStatus.Pending;
        gran.Db.SaveChanges();
        gran.Db.ChangeTracker.Clear();

        PoolResult taken = await gran.Pooling.TakeAsync();

        Assert.Equal(1, taken.Filled);

        Asset filled = gran.Db.Assets.AsNoTracking().Single();
        Assert.Equal(AssetStatus.Ready, filled.Status);
        Assert.Equal("aa11.jpg", filled.ThumbnailName);
    }

    /// <summary>
    /// Filling in never writes another computer's verdict onto a row here.
    /// </summary>
    /// <remarks>
    /// The last stand of the rule, against the row itself rather than the plan:
    /// whatever reaches it, a row filled in from another computer is a row with
    /// a picture and ready, and a row this computer has not tried is left for
    /// it to try.
    /// </remarks>
    [Fact]
    public async Task FillingInNeverWritesAVerdictAnotherComputerReached()
    {
        Asset his = Dad.Photo(@"2019\a.jpg");
        his.Status = AssetStatus.Pending;
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        int filled = await Dad.Writing.FillInAsync([Failed(Dad.KeyOf(his), his, "aa11.jpg")]);

        Assert.Equal(0, filled);

        Asset row = Dad.Db.Assets.AsNoTracking().Single();
        Assert.Equal(AssetStatus.Pending, row.Status);
        Assert.Null(row.ThumbnailName);
    }

    [Fact]
    public async Task ThePreviewIsWrittenBeforeTheTile()
    {
        // IThumbnailStore.Exists asks only about the tile, so a copy interrupted
        // between the two would leave a photograph reporting itself complete
        // with no preview - which is the file the viewer opens and the face
        // detector reads.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Mum.Pooling.GiveAsync();

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
        await Mum.Pooling.GiveAsync();

        File.Delete(Path.Combine(
            _house.SharedFolder, SharedFolderPool.ThumbsFolder, "aa", "aa11-p.jpg"));

        Assert.Empty(await Dad.Pool.NamesAsync());
    }

    [Fact]
    public async Task APathLikeRenditionNameCannotWriteOutsideThePool()
    {
        string sourceTile = Path.Combine(_house.SharedFolder, "source-tile.jpg");
        string sourcePreview = Path.Combine(_house.SharedFolder, "source-preview.jpg");
        await File.WriteAllTextAsync(sourceTile, "tile");
        await File.WriteAllTextAsync(sourcePreview, "preview");

        bool pushed = await Mum.Pool.PushAsync(
            "..escaped.jpg", sourceTile, sourcePreview);

        Assert.False(pushed);
        Assert.False(File.Exists(Path.Combine(_house.SharedFolder, "escaped.jpg")));
    }

    [Fact]
    public async Task WithNoFolderChosenItSaysSoRatherThanFailing()
    {
        using var alone = new TwoLibraries();

        PoolResult taken = await alone.Mum.Pooling.TakeAsync();
        PoolResult given = await alone.Mum.Pooling.GiveAsync();

        Assert.False(taken.Ran);
        Assert.False(given.Ran);
        Assert.Contains("no sync folder is chosen", taken.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMachineDoesNotTakeItsOwnPicturesBack()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");

        PoolResult first = await Mum.Pooling.GiveAsync();
        PoolResult taken = await Mum.Pooling.TakeAsync();
        PoolResult second = await Mum.Pooling.GiveAsync();

        Assert.Equal(1, first.Offered);
        Assert.Equal(0, taken.Filled);
        Assert.Equal(0, second.Offered);
    }

    /// <summary>
    /// Two computers giving the same copy at the same moment never leave half a
    /// picture, and whatever the race cost, the next scan puts right.
    /// </summary>
    /// <remarks>
    /// Every scan gives now, so two laptops finishing a scan together is an
    /// ordinary evening. Each file goes in under a temporary name and is renamed
    /// into place, so nobody can read half a picture under its real name. The
    /// two can both lose the race - one clearing away the temporary file the
    /// other was about to rename - and then the copy is simply not there yet:
    /// the folder is listed afresh on every give, so the next one copies it.
    /// </remarks>
    [Fact]
    public async Task TwoComputersGivingTheSameCopyAtOnceNeverLeaveHalfACopy()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");

        PoolResult[] given = await Task.WhenAll(
            Task.Run(() => Mum.Pooling.GiveAsync()),
            Task.Run(() => Dad.Pooling.GiveAsync()));

        Assert.All(given, result => Assert.True(result.Ran, result.Summary));

        string shard = Path.Combine(_house.SharedFolder, SharedFolderPool.ThumbsFolder, "aa");
        AssertWholeIfThere(Path.Combine(shard, "aa11.jpg"), "tile of aa11.jpg");
        AssertWholeIfThere(Path.Combine(shard, "aa11-p.jpg"), "preview of aa11.jpg");

        await Mum.Pooling.GiveAsync();

        Assert.Contains("aa11.jpg", await Mum.Pool.NamesAsync());
        Assert.Equal("tile of aa11.jpg", await File.ReadAllTextAsync(Path.Combine(shard, "aa11.jpg")));
        Assert.Equal(
            "preview of aa11.jpg", await File.ReadAllTextAsync(Path.Combine(shard, "aa11-p.jpg")));

        static void AssertWholeIfThere(string path, string whole)
        {
            if (File.Exists(path))
            {
                Assert.Equal(whole, File.ReadAllText(path));
            }
        }
    }

    /// <summary>
    /// A folder that goes away part way ends the phase, not the scan, and is not
    /// tried again for every picture left.
    /// </summary>
    /// <remarks>
    /// A laptop carried out of the house mid-scan. Each copy to a folder that is
    /// not there fails only after the operating system gives up on it, and there
    /// are twenty thousand of them - so the first failure asks whether the folder
    /// is still there, and on hearing it is not, the phase stops and says why.
    /// </remarks>
    [Fact]
    public async Task AFolderThatGoesAwayPartWayEndsThePhaseAndSaysWhy()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Mum.Prepared(@"2019\b.jpg", "bb22.jpg");
        Mum.Prepared(@"2019\c.jpg", "cc33.jpg");

        var away = new GoesAway(Mum.Pool, everyCopyFails: true);

        PoolResult given = await Through(away).GiveAsync();

        Assert.False(given.Ran);
        Assert.Contains("Stopped part way", given.Summary, StringComparison.Ordinal);
        Assert.Equal(1, away.Copies);
    }

    /// <summary>And one copy failing, with the folder still there, stops nothing.</summary>
    [Fact]
    public async Task OneCopyThatFailsDoesNotStopTheRest()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Mum.Prepared(@"2019\b.jpg", "bb22.jpg");
        Mum.Prepared(@"2019\c.jpg", "cc33.jpg");

        var once = new GoesAway(Mum.Pool, everyCopyFails: false);

        PoolResult given = await Through(once).GiveAsync();

        Assert.True(given.Ran);
        Assert.Equal(2, given.Offered);
        Assert.Equal(3, once.Copies);
    }

    /// <summary>
    /// A folder that fails outright part way is said to have, and does not fail
    /// the scan it is a phase of.
    /// </summary>
    [Fact]
    public async Task AFolderThatFailsPartWayIsReportedRatherThanThrown()
    {
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");

        var failing = new GoesAway(
            Mum.Pool, everyCopyFails: false,
            listing: new IOException("The network path was not found."));

        PoolResult given = await Through(failing).GiveAsync();

        Assert.False(given.Ran);
        Assert.Equal(
            "Stopped part way, because the sync folder could not be used: The network path "
            + "was not found. What was copied is kept.",
            given.Summary);
    }

    /// <summary>
    /// What the build before gave for a photograph it could not read, while the
    /// row still named the picture it used to have.
    /// </summary>
    private static PreparedFact Failed(AssetKey photo, Asset row, string thumbnailName) =>
        new(
            photo,
            row.Length,
            row.ModifiedUtc,
            null,
            thumbnailName,
            row.Width ?? 0,
            row.Height ?? 0,
            null,
            null,
            null,
            null,
            null,
            AssetStatus.Failed,
            []);

    /// <summary>Mum's two scan phases, giving through a folder a test controls.</summary>
    private ShareRenditionsHandler Through(IRenditionPool pool) =>
        new(Mum.Index, Mum.Decisions, Mum.Writing, pool, Mum.Thumbnails, Mum.Waiting,
            Mum.Models, Mum.Exchange);

    /// <summary>
    /// The real folder, except that copying into it fails - every time, after
    /// which it says it is not there any more, or once only, while it stays -
    /// and listing it may fail outright.
    /// </summary>
    private sealed class GoesAway : IRenditionPool
    {
        private readonly IRenditionPool _real;
        private readonly bool _everyCopyFails;
        private readonly IOException? _listing;
        private bool _gone;

        public GoesAway(IRenditionPool real, bool everyCopyFails, IOException? listing = null) =>
            (_real, _everyCopyFails, _listing) = (real, everyCopyFails, listing);

        /// <summary>How many copies were attempted.</summary>
        public int Copies { get; private set; }

        public Task<ExchangeReadiness> ReadinessAsync(CancellationToken cancellationToken = default) =>
            _gone
                ? Task.FromResult(ExchangeReadiness.Not("That folder cannot be reached."))
                : _real.ReadinessAsync(cancellationToken);

        public Task PublishAsync(PreparedSet mine, CancellationToken cancellationToken = default) =>
            _real.PublishAsync(mine, cancellationToken);

        public Task<IReadOnlyList<PreparedSet>> FetchAsync(
            CancellationToken cancellationToken = default) =>
            _real.FetchAsync(cancellationToken);

        public Task<bool> PublishFacesAsync(
            FaceSet mine, CancellationToken cancellationToken = default) =>
            _real.PublishFacesAsync(mine, cancellationToken);

        public Task<IReadOnlyList<FaceSet>> FetchFacesAsync(
            CancellationToken cancellationToken = default) =>
            _real.FetchFacesAsync(cancellationToken);

        public Task<IReadOnlyCollection<string>> NamesAsync(
            CancellationToken cancellationToken = default) =>
            _listing is null ? _real.NamesAsync(cancellationToken) : throw _listing;

        public Task<bool> PushAsync(
            string thumbnailName,
            string tilePath,
            string previewPath,
            CancellationToken cancellationToken = default)
        {
            Copies++;

            if (_everyCopyFails)
            {
                _gone = true;
                return Task.FromResult(false);
            }

            return Copies == 1
                ? Task.FromResult(false)
                : _real.PushAsync(thumbnailName, tilePath, previewPath, cancellationToken);
        }

        public Task<bool> PullAsync(
            string thumbnailName,
            string tilePath,
            string previewPath,
            CancellationToken cancellationToken = default) =>
            _real.PullAsync(thumbnailName, tilePath, previewPath, cancellationToken);
    }

    public void Dispose() => _house.Dispose();
}
