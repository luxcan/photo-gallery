using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Refresh;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Application.UseCases.Videos;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Search;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Imaging;
using PhotoGallery.Infrastructure.Models;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Sharing;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Sharing;

namespace PhotoGallery.Tests.Application;

public sealed class RefreshLibraryHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _photosRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;
    private readonly SqliteAssetRepository _assets;
    private readonly SqliteGalleryReader _reader;
    private readonly WorkingFolder _workingFolder;
    private readonly MediaFileWalker _walker;
    private readonly FileSystemThumbnailStore _store;
    private readonly StubGenerator _generator = new();
    private readonly StubExtractor _extractor = new();
    private readonly FakeSourceAvailability _sources = new();

    public RefreshLibraryHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-refresh-{Guid.NewGuid():N}");
        string workingFolderRoot = Path.Combine(_tempRoot, "library");
        _photosRoot = Path.Combine(_tempRoot, "photos");
        Directory.CreateDirectory(workingFolderRoot);
        Directory.CreateDirectory(_photosRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(workingFolderRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();

        _index = new SqliteLibraryIndex(_db);
        _assets = new SqliteAssetRepository(_db);
        _reader = new SqliteGalleryReader(_db);
        _workingFolder = new WorkingFolder(workingFolderRoot);
        _workingFolder.EnsureCreated();
        _walker = new MediaFileWalker(_workingFolder);
        _store = new FileSystemThumbnailStore(_workingFolder);
    }

    [Fact]
    public async Task Refresh_IndexesAndThenPrepares()
    {
        // The point of joining the two halves: one call ends with pictures ready
        // to look at, rather than rows with nothing behind them.
        WriteMedia("a.jpg");
        WriteMedia("b.jpg");
        PhotoSource source = await AddSourceAsync();

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(2, result.Added);
        Assert.Equal(2, result.Built);
        Assert.False(result.WasCancelled);
        Assert.Equal(2, _store.ListStoredNames().Count);
        Assert.All(await StatusesAsync(), status => Assert.Equal(AssetStatus.Ready, status));
    }

    [Fact]
    public async Task Refresh_StoppedWhileIndexingNeverStartsPreparing()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        RefreshResult result = await NewHandler().HandleAsync([source.Id], null, cancellation.Token);

        // Generating from a half-finished crawl would work from an index known to
        // be incomplete, so it is not attempted at all.
        Assert.True(result.WasCancelled);
        Assert.Null(result.Generated);
        Assert.Empty(_store.ListStoredNames());
    }

    [Fact]
    public async Task Refresh_StoppedWhilePreparingKeepsWhatItFinished()
    {
        for (int i = 0; i < 40; i++)
        {
            WriteMedia($"photo-{i}.jpg");
        }

        PhotoSource source = await AddSourceAsync();

        using var cancellation = new CancellationTokenSource();
        int made = 0;
        _generator.OnGenerate = () =>
        {
            // Part way through the second batch of twenty, so the first batch has
            // certainly been written and the stop lands mid-flight. Cancelling
            // inside the first batch is a race between eight parallel workers and
            // proves nothing either way.
            if (Interlocked.Increment(ref made) == 25)
            {
                cancellation.Cancel();
            }
        };

        RefreshResult result = await NewHandler().HandleAsync([source.Id], null, cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(40, result.Added);
        Assert.True(result.Built < 40, "the pass was not actually interrupted");
        Assert.True(result.Built > 0, "nothing was kept from before the stop");
    }

    [Fact]
    public async Task Refresh_ResumesWhatAStopLeftPending()
    {
        for (int i = 0; i < 40; i++)
        {
            WriteMedia($"photo-{i}.jpg");
        }

        PhotoSource source = await AddSourceAsync();

        using var cancellation = new CancellationTokenSource();
        int made = 0;
        _generator.OnGenerate = () =>
        {
            // Part way through the second batch of twenty, so the first batch has
            // certainly been written and the stop lands mid-flight. Cancelling
            // inside the first batch is a race between eight parallel workers and
            // proves nothing either way.
            if (Interlocked.Increment(ref made) == 25)
            {
                cancellation.Cancel();
            }
        };

        RefreshResult stopped = await NewHandler()
            .HandleAsync([source.Id], null, cancellation.Token);
        Assert.True(stopped.WasCancelled);

        // Running it again carries on rather than starting over: the crawl finds
        // nothing new, and only the pictures left pending are made.
        _generator.OnGenerate = null;
        RefreshResult finished = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(0, finished.Added);
        Assert.Equal(40 - stopped.Built, finished.Built);
        Assert.Equal(40, _store.ListStoredNames().Count);
        Assert.All(await StatusesAsync(), status => Assert.Equal(AssetStatus.Ready, status));
    }

    [Fact]
    public async Task Refresh_MarksAnUndecodableFileFailedSoItIsNotOpenedAgain()
    {
        WriteMedia("good.jpg");
        WriteMedia("broken.jpg");
        _generator.Undecodable.Add("broken.jpg");
        PhotoSource source = await AddSourceAsync();

        RefreshResult first = await NewHandler().HandleAsync([source.Id]);
        Assert.Equal(1, first.Built);

        int openedFirstTime = _generator.Generated;
        RefreshResult second = await NewHandler().HandleAsync([source.Id]);

        // The broken file is a fact now, not a candidate: a second run does not
        // touch it, so one bad file cannot cost a read on every pass for ever.
        Assert.Equal(0, second.Built);
        Assert.Equal(openedFirstTime, _generator.Generated);
        Assert.Contains(AssetStatus.Failed, await StatusesAsync());
    }

    [Fact]
    public async Task Refresh_OfAnUnreachableFolderChangesNothing()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        await NewHandler().HandleAsync([source.Id]);

        Directory.Move(_photosRoot, _photosRoot + "-away");
        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(1, result.Unavailable);
        Assert.Equal(0, result.Removed);
        Assert.Equal(1, await _assets.CountAsync(source.Id));
        Assert.Single(_store.ListStoredNames());
    }

    /// <summary>
    /// Photographs on a source that is away are left for the next scan, not
    /// written off.
    /// </summary>
    /// <remarks>
    /// Found on the family library: a scan with the drive switched off turned
    /// 13,990 photographs Failed - unreadable for good, so they stayed blank
    /// after the drive came back.
    /// </remarks>
    [Fact]
    public async Task Refresh_WithTheSourceAway_LeavesItsPhotosToBePreparedLater()
    {
        WriteMedia("a.jpg");
        WriteMedia("b.jpg");
        PhotoSource source = await AddSourceAsync();
        _sources.Away.Add(source.Path);

        RefreshResult away = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(0, _generator.Generated);
        Assert.Equal(0, away.Built);
        Assert.DoesNotContain(AssetStatus.Failed, await StatusesAsync());

        _sources.Away.Clear();
        RefreshResult back = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(2, back.Built);
    }

    /// <summary>
    /// A refresh skips describing entirely when the search models are absent.
    /// </summary>
    /// <remarks>
    /// Scanning is the core action of the app and has to work on a machine that
    /// has never downloaded 1.7 GB of weights. Nothing is read, nothing fails,
    /// and the result says the step did not happen rather than that it found
    /// nothing.
    /// </remarks>
    [Fact]
    public async Task Refresh_WithoutTheSearchModelsStillScansAndPrepares()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Built);
        Assert.True(result.Described!.ModelsMissing);
        Assert.Equal(0, result.NowSearchable);
        Assert.DoesNotContain("described", result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scan with nothing outstanding never asks whether the models are there.
    /// </summary>
    /// <remarks>
    /// Answering that question means digesting 1.7 GB of weights, and this runs
    /// at the end of every scan. A rescan that finds nothing new - which is most
    /// of them - must not pay it to discover it had nothing to do.
    /// </remarks>
    [Fact]
    public async Task Refresh_WithNothingLeftToDescribeDoesNotLookForTheSearchModels()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        await NewHandler().HandleAsync([source.Id]);

        // Described for real, so that the next run genuinely has nothing
        // outstanding. Without this the picture stays pending forever and the
        // question below could never be asked.
        Asset asset = await _db.Assets.AsNoTracking().SingleAsync();
        await new SqliteContentRepository(_db).SaveAsync(
        [
            new ContentScanUpdate(
                asset.ThumbnailName!, [asset.Id], SomeVector(), DateTime.UtcNow),
        ]);

        var counting = new CountsWhatItIsAsked();
        RefreshResult again = await NewHandler(counting).HandleAsync([source.Id]);

        Assert.Equal(0, again.Added);
        Assert.Equal(0, counting.AskedAboutSearch);
        Assert.False(again.Described!.ModelsMissing);
    }

    /// <summary>
    /// The whole point of the video phase: one action, and the videos it found
    /// are ready too.
    /// </summary>
    /// <remarks>
    /// A scan used to index a clip, park it, and leave making its picture to a
    /// second button - so a user who scanned came away with videos in the index,
    /// nothing in the grid, and no reason for it.
    /// </remarks>
    [Fact]
    public async Task Refresh_AlsoPreparesTheVideosItIndexed()
    {
        _extractor.Decodes = true;
        WriteMedia("a.jpg");
        WriteMedia("holiday.mp4");
        PhotoSource source = await AddSourceAsync();

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Built);
        Assert.Equal(1, result.VideosPrepared);
        Assert.Equal(1, _extractor.Opened);

        // Named in the line the user reads, not only in the object.
        Assert.Contains("1 videos prepared", result.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// A clip that could not be reached is not written off by the scan.
    /// </summary>
    /// <remarks>
    /// The share blinking during a scan must cost nothing permanent: the video
    /// keeps its pending status and the next scan offers it again. This is the
    /// same promise the pass made when it had its own button, and moving it into
    /// the scan must not quietly weaken it.
    /// </remarks>
    [Fact]
    public async Task Refresh_LeavesAnUnreachableVideoForNextTime()
    {
        WriteMedia("holiday.mp4");
        PhotoSource source = await AddSourceAsync();

        RefreshResult first = await NewHandler().HandleAsync([source.Id]);
        Assert.Equal(0, first.VideosPrepared);

        _extractor.Decodes = true;
        RefreshResult second = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(1, second.VideosPrepared);
    }

    /// <summary>
    /// A stop that lands in one of the late phases is answered, not thrown.
    /// </summary>
    /// <remarks>
    /// Each of the three phases after generating opens with a query that takes
    /// the token, and each of those queries sat outside its own handler's try.
    /// Stopping there raised <see cref="OperationCanceledException"/> out of the
    /// refresh entirely - past a caller whose catch filter does not name it, and
    /// onto the dispatcher. Survivable while these were buttons somebody pressed
    /// on purpose; a crash on the app's main action now that they are its tail.
    ///
    /// <para>Cancelling part way through generating would prove nothing: the
    /// refresh returns at the generating check and never reaches these phases at
    /// all. So the stop is hung on each phase's own opening report, which is
    /// emitted immediately before that phase is called - the token is therefore
    /// certain to be cancelled when its query runs, and only there.</para>
    /// </remarks>
    [Theory]
    [InlineData(RefreshPhase.Locating)]
    [InlineData(RefreshPhase.PreparingVideos)]
    [InlineData(RefreshPhase.FindingFaces)]
    [InlineData(RefreshPhase.Collecting)]
    public async Task Refresh_StoppedAsAPhaseBeginsAnswersRatherThanThrowing(RefreshPhase phase)
    {
        WriteMedia("a.jpg");
        WriteMedia("holiday.mp4");
        _extractor.Decodes = true;
        PhotoSource source = await AddSourceAsync();

        using var cancellation = new CancellationTokenSource();
        var stopWhenItStarts = new StopsAt(phase, cancellation);

        // Returning at all is the assertion. Before the guards this threw, and
        // the throw escaped every catch between here and the window.
        RefreshResult result = await NewHandler()
            .HandleAsync([source.Id], stopWhenItStarts, cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.True(stopWhenItStarts.Fired, $"the refresh never reached {phase}");
    }

    /// <summary>Trips the token the moment a named phase reports for the first time.</summary>
    private sealed class StopsAt : IProgress<RefreshProgress>
    {
        private readonly RefreshPhase _phase;
        private readonly CancellationTokenSource _cancellation;

        public StopsAt(RefreshPhase phase, CancellationTokenSource cancellation) =>
            (_phase, _cancellation) = (phase, cancellation);

        /// <summary>Whether the phase was ever reached, so a silent miss fails.</summary>
        public bool Fired { get; private set; }

        public void Report(RefreshProgress value)
        {
            if (value.Phase != _phase || Fired)
            {
                return;
            }

            Fired = true;
            _cancellation.Cancel();
        }
    }

    /// <summary>
    /// A scan says which parts it could not run, rather than looking complete.
    /// </summary>
    /// <remarks>
    /// These were buttons: not pressing one was a choice, and the library being
    /// short of descriptions or faces was the visible consequence of it. Now
    /// that they are phases nobody chooses, a scan that silently did five things
    /// out of six reads as a scan that did everything.
    /// </remarks>
    [Fact]
    public async Task Refresh_SaysWhichPartsCouldNotRun()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        // Both models absent, which is this test class's normal state.
        Assert.True(result.Described!.ModelsMissing);
        Assert.True(result.Faces!.ModelsMissing);
        Assert.Contains(
            "the search model and the face model are not installed",
            result.Summary,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The order of the phases, which is the whole of what makes a stop cheap.
    /// </summary>
    /// <remarks>
    /// Cheap work first, so that stopping loses as little as possible, except
    /// where one phase reads what another writes: faces are looked for in a
    /// clip's keyframes as well as a photograph's preview, so they must come
    /// after the videos or every face in every video waits for the next scan.
    /// </remarks>
    [Fact]
    public async Task Refresh_RunsItsPhasesCheapestFirstAndFacesLast()
    {
        _extractor.Decodes = true;
        WriteMedia("a.jpg");
        WriteMedia("holiday.mp4");
        PhotoSource source = await AddSourceAsync();

        var watching = new PhasesSeen();
        await NewHandler().HandleAsync([source.Id], watching);

        // Describing is absent on purpose: these tests run with no search models,
        // so that phase returns without reporting anything. A phase with nothing
        // to do saying nothing is the behaviour, not a gap in this list.
        Assert.Equal(
            [
                RefreshPhase.Indexing,
                RefreshPhase.Generating,
                RefreshPhase.Locating,
                RefreshPhase.PreparingVideos,
                RefreshPhase.FindingFaces,
                RefreshPhase.Collecting,
            ],
            watching.InOrder);
    }

    /// <summary>
    /// Records the phases in the order they were reported.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Progress{T}"/>, which posts to the synchronisation context
    /// and so may deliver its last reports after the await has returned - which
    /// made this test pass or fail depending on timing. The handler calls
    /// <see cref="IProgress{T}.Report"/> directly, so recording it here is exact.
    /// </remarks>
    private sealed class PhasesSeen : IProgress<RefreshProgress>
    {
        private readonly List<RefreshPhase> _seen = [];

        public IReadOnlyList<RefreshPhase> InOrder => _seen;

        public void Report(RefreshProgress value)
        {
            if (_seen.Count == 0 || _seen[^1] != value.Phase)
            {
                _seen.Add(value.Phase);
            }
        }
    }

    [Fact]
    public async Task Refresh_AppliesAnswersThatWereWaitingForTheirPhotographs()
    {
        // That the sweep is a phase of the scan rather than a button, which is
        // the whole of its value: nobody would think to press one, because the
        // thing it repairs happened days ago on somebody else's laptop.
        //
        // A rejection rather than a name, because it needs only the photograph -
        // these tests have no face models, so no face would ever be found for a
        // name to land on.
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();

        var waiting = new SharedAlbumRejection(
            new AssetKey(source.SharedId, "a.jpg"),
            "2020-01",
            new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid());

        // Parked through the code that parks them, so the row under test is the
        // row the merge would actually have written.
        await new SqliteDecisionRepository(_db).ApplyAsync(
            MergePlan.Nothing with
            {
                Held = new HeldAnswers([], [], [], [], [waiting]),
            });

        Assert.Single(_db.HeldDecisions);

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(1, result.AnswersApplied);
        Assert.Equal(0, result.AnswersWaiting);
        Assert.Contains(", 1 answer applied", result.Summary);
        Assert.DoesNotContain("settled", result.Summary);

        _db.ChangeTracker.Clear();
        AlbumRejection landed = _db.AlbumRejections.Single();
        Assert.Equal("2020-01", landed.ProposalKey);
        Assert.Empty(_db.HeldDecisions);
    }

    [Fact]
    public async Task Refresh_SaysWhenAnswersAreStillWaitingForPhotographsItDoesNotHave()
    {
        // The one number the summary must never hide: it is the difference
        // between "nothing to do" and "an evening's work is waiting for a folder
        // nobody has added".
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();

        var waiting = new SharedAlbumRejection(
            new AssetKey(source.SharedId, @"only on her laptop.jpg"),
            "2020-01",
            new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid());

        await new SqliteDecisionRepository(_db).ApplyAsync(
            MergePlan.Nothing with
            {
                Held = new HeldAnswers([], [], [], [], [waiting]),
            });

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(0, result.AnswersApplied);
        Assert.Equal(1, result.AnswersWaiting);
        Assert.Contains(
            "1 answer is still waiting for something this library does not have yet",
            result.Summary);

        // Not the old sentence, which named one cause for every held answer and
        // was false for most of them on a real library.
        Assert.DoesNotContain("has not indexed", result.Summary);
        Assert.Single(_db.HeldDecisions);
    }

    /// <summary>
    /// An answer the library already holds is let go, and said to be settled
    /// rather than applied.
    /// </summary>
    /// <remarks>
    /// "Applied" is the one number in this line about another machine's work
    /// reaching this one, so it may only count what was written. An answer that
    /// was already true here wrote nothing, and saying it was applied would make
    /// the number mean "no longer waiting" instead.
    ///
    /// <para>Nor does it say a later answer settled it. The waiting rejection is
    /// the later of the two here, and the line once said "settled by later
    /// answers" about exactly this.</para>
    /// </remarks>
    [Fact]
    public async Task Refresh_SaysWhenAWaitingAnswerWasSettledRatherThanApplied()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        await NewHandler().HandleAsync([source.Id]);

        // This library already said the same thing about the same photograph.
        _db.AlbumRejections.Add(new AlbumRejection
        {
            AssetId = _db.Assets.Single().Id,
            ProposalKey = "2020-01",
            RejectedUtc = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var waiting = new SharedAlbumRejection(
            new AssetKey(source.SharedId, "a.jpg"),
            "2020-01",
            new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid());

        await new SqliteDecisionRepository(_db).ApplyAsync(
            MergePlan.Nothing with
            {
                Held = new HeldAnswers([], [], [], [], [waiting]),
            });

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(0, result.AnswersApplied);
        Assert.Equal(1, result.AnswersSettled);
        Assert.Equal(0, result.AnswersWaiting);
        Assert.Contains(", 1 answer needed no change", result.Summary);
        Assert.DoesNotContain("later", result.Summary);
        Assert.DoesNotContain("applied", result.Summary);
        Assert.Empty(_db.HeldDecisions);
        Assert.Single(_db.AlbumRejections);
    }

    /// <summary>
    /// An answer settled by a deletion is said to have needed no change, not to
    /// have been beaten by a later answer.
    /// </summary>
    /// <remarks>
    /// A settled answer may have been true here already, beaten here, or about an
    /// album or a person deleted here, and the scan log counts them in one
    /// number. So the words for that number name no reason: one reason named for
    /// all three is false for two of them.
    /// </remarks>
    [Fact]
    public async Task Refresh_NamesNoReasonForAnAnswerADeletionSettled()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();

        // An album this library made and then deleted, while a move into it
        // waited for its photograph.
        var deleted = new Album
        {
            PublicId = Guid.NewGuid(),
            Name = "Genting",
            StartUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Kind = AlbumKind.Period,
            Origin = AlbumOrigin.Made,
            NamedUtc = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
            BuiltUtc = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
            DeletedUtc = new DateTime(2026, 3, 3, 9, 0, 0, DateTimeKind.Utc),
        };
        _db.Albums.Add(deleted);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var waiting = new SharedAlbumMembership(
            new AssetKey(source.SharedId, "a.jpg"),
            deleted.PublicId,
            new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid());

        await new SqliteDecisionRepository(_db).ApplyAsync(
            MergePlan.Nothing with
            {
                Held = HeldAnswers.None with { Memberships = [waiting] },
            });

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(0, result.AnswersApplied);
        Assert.Equal(1, result.AnswersSettled);
        Assert.Contains(", 1 answer needed no change", result.Summary);
        Assert.DoesNotContain("later", result.Summary);
        Assert.Empty(_db.HeldDecisions);
        Assert.Empty(_db.AlbumMembers);
    }

    /// <summary>
    /// A scan leaves this library's small copies in the sync folder.
    /// </summary>
    /// <remarks>
    /// They used to go only when somebody pressed a button that showed only on
    /// a laptop with photographs still to prepare, and was worded as taking -
    /// so a laptop with everything prepared never gave, the folder never held a
    /// picture, and no face ever travelled.
    /// </remarks>
    [Fact]
    public async Task Refresh_LeavesItsSmallCopiesInTheSyncFolder()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        await SyncThroughAsync();

        await NewHandler().HandleAsync([source.Id]);

        string made = Assert.Single(_store.ListStoredNames());
        Assert.Contains(made, await new SharedFolderPool(_index, _workingFolder).NamesAsync());
    }

    /// <summary>
    /// A scan takes the small copies another computer has made before it makes
    /// any itself.
    /// </summary>
    /// <remarks>
    /// Taking them after the generating phase would be no use at all: by then
    /// this laptop has spent the hour reading every original to make copies of
    /// its own.
    /// </remarks>
    [Fact]
    public async Task Refresh_TakesTheSmallCopiesAnotherComputerMadeRatherThanMakingThem()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        string shared = await SyncThroughAsync();

        using var mum = new Library(
            Path.Combine(_tempRoot, "mum"), "Mum's laptop", source.SharedId);
        mum.SharesThrough(shared);
        await PreparedOnAsync(mum, "a.jpg", "aa11.jpg");

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(0, result.Built);
        Assert.Equal(0, _generator.Generated);
        Assert.True(_store.Exists("aa11.jpg"));
        Assert.Equal("aa11.jpg", (await _db.Assets.AsNoTracking().SingleAsync()).ThumbnailName);
    }

    /// <summary>
    /// A scan still makes the small copy of a photograph another computer could
    /// not decode.
    /// </summary>
    /// <remarks>
    /// Taking comes before generating, so a row taken as "this never decoded"
    /// was one this laptop never tried - generating skips a failed row - and
    /// nothing tried it again until the file changed. The other laptop may
    /// simply lack a codec this one has.
    /// </remarks>
    [Fact]
    public async Task Refresh_MakesTheSmallCopyAnotherComputerCouldNotDecode()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        string shared = await SyncThroughAsync();

        using var mum = new Library(
            Path.Combine(_tempRoot, "mum"), "Mum's laptop", source.SharedId);
        mum.SharesThrough(shared);

        var file = new FileInfo(Path.Combine(_photosRoot, "a.jpg"));
        Asset hers = mum.Photo("a.jpg");
        hers.Length = file.Length;
        hers.ModifiedUtc = file.LastWriteTimeUtc;
        hers.Status = AssetStatus.Failed;
        mum.Db.SaveChanges();
        mum.Db.ChangeTracker.Clear();
        await mum.Pooling.GiveAsync();

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(1, result.Built);
        Assert.Equal(1, _generator.Generated);

        Asset mine = await _db.Assets.AsNoTracking().SingleAsync();
        Assert.Equal(AssetStatus.Ready, mine.Status);
        Assert.NotNull(mine.ThumbnailName);
    }

    /// <summary>
    /// A computer that lost its own small copies and then scanned with its
    /// originals out of reach decides nothing for the next one.
    /// </summary>
    /// <remarks>
    /// Found driving the app on three libraries. Mum's had everything ready and
    /// in the sync folder, and Dad's had it too. Then Mum's lost its small
    /// copies and scanned with the photographs' folder out of reach, which marked
    /// every photograph "could not be read" and left on each row the name of the
    /// copy it used to have - and it gave again. A third laptop then scanned:
    /// Mum's facts were the newest in the folder, so they beat Dad's, and every
    /// row it filled in was "could not be read" with both its files on the disk.
    /// Nothing describes, places, groups or looks for faces in such a row, so
    /// that laptop's library was pictures and nothing else, for good.
    /// </remarks>
    [Fact]
    public async Task Refresh_TakesNoVerdictFromAComputerThatLostItsCopiesAndScannedOffline()
    {
        WriteMedia("a.jpg");
        WriteMedia("b.jpg");
        PhotoSource source = await AddSourceAsync();
        string shared = await SyncThroughAsync();

        using var mum = new Library(
            Path.Combine(_tempRoot, "mum"), "Mum's laptop", source.SharedId);
        mum.SharesThrough(shared);
        await PreparedOnAsync(mum, "a.jpg", "aa11.jpg");
        await PreparedOnAsync(mum, "b.jpg", "bb22.jpg");

        using var dad = new Library(
            Path.Combine(_tempRoot, "dad"), "Dad's laptop", source.SharedId);
        dad.SharesThrough(shared);
        await PreparedOnAsync(dad, "a.jpg", "aa11.jpg");
        await PreparedOnAsync(dad, "b.jpg", "bb22.jpg");

        // Mum's small copies go, and her own preparing pass cannot reach the
        // originals to make them again: her library has none of the files.
        foreach (string name in new[] { "aa11.jpg", "bb22.jpg" })
        {
            File.Delete(mum.Thumbnails.ResolveTilePath(name));
            File.Delete(mum.Thumbnails.ResolvePreviewPath(name));
        }

        await new BuildThumbnailsHandler(
                new SqliteGalleryReader(mum.Db),
                new SqliteAssetRepository(mum.Db),
                mum.Thumbnails,
                new WindowsThumbnailGenerator(), _sources)
            .HandleAsync();
        mum.Db.ChangeTracker.Clear();

        Assert.All(mum.Db.Assets.AsNoTracking(), row =>
        {
            Assert.Equal(AssetStatus.Failed, row.Status);
            Assert.NotNull(row.ThumbnailName);
        });

        await mum.Pooling.GiveAsync();

        StubModelStore searchOnly = new StubModelStore()
            .Without(ModelId.FaceDetection)
            .Without(ModelId.FaceRecognition);

        foreach (ModelId id in FeatureModels.Of(ModelFeature.ContentSearch))
        {
            searchOnly.Running(id, "search-v1");
        }

        RefreshResult result = await NewHandler(searchOnly, encoder: new DescribesEverything())
            .HandleAsync([source.Id]);

        // Ready, and then everything a ready row is for: placed, and described.
        List<Asset> mine = await _db.Assets.AsNoTracking().ToListAsync();
        Assert.Equal(2, mine.Count);
        Assert.All(mine, row =>
        {
            Assert.Equal(AssetStatus.Ready, row.Status);
            Assert.NotNull(row.ThumbnailName);
            Assert.NotNull(row.LocationReadUtc);
        });

        Assert.Equal(2, result.NowSearchable);
        Assert.Equal(2, await _db.PhotoContent.CountAsync());
    }

    /// <summary>
    /// With a sync folder, the scan takes straight after the crawl and gives
    /// last of all.
    /// </summary>
    /// <remarks>
    /// Taking before generating is the whole of its value, and giving last is
    /// what makes a stop in it - the first time, gigabytes - cost nothing else.
    /// Without a folder neither phase reports at all, which the test above
    /// pins: a phase with nothing to do says nothing.
    /// </remarks>
    [Fact]
    public async Task Refresh_WithASyncFolderTakesFirstAndGivesLast()
    {
        _extractor.Decodes = true;
        WriteMedia("a.jpg");
        WriteMedia("holiday.mp4");
        PhotoSource source = await AddSourceAsync();
        await SyncThroughAsync();

        var watching = new PhasesSeen();
        await NewHandler().HandleAsync([source.Id], watching);

        Assert.Equal(
            [
                RefreshPhase.Indexing,
                RefreshPhase.TakingCopies,
                RefreshPhase.Generating,
                RefreshPhase.Locating,
                RefreshPhase.PreparingVideos,
                RefreshPhase.FindingFaces,
                RefreshPhase.Collecting,
                RefreshPhase.GivingCopies,
            ],
            watching.InOrder);
    }

    /// <summary>
    /// The next scan gives only what the folder does not have.
    /// </summary>
    /// <remarks>
    /// Every scan gives now, so a scan that copied the whole library again each
    /// time would make the app's main action gigabytes long for ever. The pool
    /// is listed once and only the names it lacks are copied.
    /// </remarks>
    [Fact]
    public async Task Refresh_GivesOnlyWhatIsNewOnTheNextScan()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        await SyncThroughAsync();

        RefreshResult first = await NewHandler().HandleAsync([source.Id]);

        WriteMedia("b.jpg");
        RefreshResult second = await NewHandler().HandleAsync([source.Id]);

        Assert.Equal(1, first.PicturesGiven);
        Assert.Equal(1, second.PicturesGiven);
        Assert.Equal(2, (await new SharedFolderPool(_index, _workingFolder).NamesAsync()).Count);
    }

    /// <summary>
    /// A sync folder that cannot be reached costs the scan one line and nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// A laptop out of the house, or a drive that is off: the scan is the app's
    /// main action, and it must finish and make its own pictures exactly as it
    /// would with no folder at all.
    /// </remarks>
    [Fact]
    public async Task Refresh_WithTheSyncFolderOutOfReachStillFinishes()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        string shared = await SyncThroughAsync();
        Directory.Delete(shared);

        RefreshResult result = await NewHandler().HandleAsync([source.Id]);

        Assert.False(result.WasCancelled);
        Assert.Equal(1, result.Built);
        Assert.False(result.Taken!.Ran);
        Assert.Null(result.Given);
        Assert.Single(
            result.Timings,
            line => line.Contains("cannot be reached", StringComparison.Ordinal));
    }

    /// <summary>
    /// A sync folder that does not answer is not waited on past the deadline.
    /// </summary>
    /// <remarks>
    /// The question underneath cannot be cancelled and an address nothing answers
    /// holds it for as long as the operating system likes. A folder that never
    /// answers here stands for that: the scan goes on without it and finishes.
    /// </remarks>
    [Fact]
    public async Task Refresh_WithTheSyncFolderSlowToAnswerGoesOnWithoutIt()
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        await SyncThroughAsync();

        RefreshResult result = await NewHandler(pool: new NeverAnswers())
            .HandleAsync([source.Id])
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(result.WasCancelled);
        Assert.Equal(1, result.Built);
        Assert.Contains("did not answer", result.Taken!.Summary, StringComparison.Ordinal);
        Assert.Null(result.Given);
    }

    /// <summary>
    /// A stop as either phase at the sync folder begins is answered, not thrown.
    /// </summary>
    [Theory]
    [InlineData(RefreshPhase.TakingCopies)]
    [InlineData(RefreshPhase.GivingCopies)]
    public async Task Refresh_StoppedAsTheSmallCopiesBeginAnswersRatherThanThrowing(
        RefreshPhase phase)
    {
        WriteMedia("a.jpg");
        PhotoSource source = await AddSourceAsync();
        await SyncThroughAsync();

        using var cancellation = new CancellationTokenSource();
        var stopWhenItStarts = new StopsAt(phase, cancellation);

        RefreshResult result = await NewHandler()
            .HandleAsync([source.Id], stopWhenItStarts, cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.True(stopWhenItStarts.Fired, $"the refresh never reached {phase}");
    }

    /// <summary>
    /// A sync folder that is chosen and never answers, as an address on a
    /// network that has gone does.
    /// </summary>
    private sealed class NeverAnswers : IRenditionPool
    {
        public Task<ExchangeReadiness> ReadinessAsync(CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<ExchangeReadiness>().Task;

        public Task PublishAsync(PreparedSet mine, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The scan wrote to a folder that never answered.");

        public Task<IReadOnlyList<PreparedSet>> FetchAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The scan read a folder that never answered.");

        public Task<bool> PublishFacesAsync(
            FaceSet mine, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The scan wrote to a folder that never answered.");

        public Task<IReadOnlyList<FaceSet>> FetchFacesAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The scan read a folder that never answered.");

        public Task<IReadOnlyCollection<string>> NamesAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The scan listed a folder that never answered.");

        public Task<bool> PushAsync(
            string thumbnailName,
            string tilePath,
            string previewPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The scan wrote to a folder that never answered.");

        public Task<bool> PullAsync(
            string thumbnailName,
            string tilePath,
            string previewPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The scan read a folder that never answered.");
    }

    /// <summary>Any unit vector; what it points at is beside the point here.</summary>
    private static ContentEmbedding SomeVector()
    {
        float[] values = new float[ContentEmbedding.Dimensions];
        values[0] = 1f;
        return new ContentEmbedding(values);
    }

    /// <param name="pool">
    /// The sync folder as the scan sees it: the real one unless a test needs a
    /// folder that will not answer.
    /// </param>
    /// <param name="encoder">
    /// What describes a picture, for a test whose models say the search models
    /// are installed. Left out, nothing may be described at all.
    /// </param>
    private RefreshLibraryHandler NewHandler(
        IModelStore? models = null, IRenditionPool? pool = null, IContentEncoder? encoder = null) =>
        new(new ScanPhotoSourceHandler(
                _index,
                _assets,
                _walker,
                _store,
                new SqliteDecisionReader(_db),
                new SqliteDecisionRepository(_db)),
            new BuildThumbnailsHandler(_reader, _assets, _store, _generator, _sources),
            new IndexContentHandler(
                _reader,
                _store,
                encoder ?? new NeverAsked(),
                new SqliteContentRepository(_db),
                models ?? new NoModels()),
            new LocatePhotosHandler(
                _reader,
                new NoCoordinates(),
                new NowhereNamed(),
                new SqlitePlaceRepository(_db),
                _assets,
                _sources),
            new BuildVideoKeyframesHandler(_reader, _assets, _store, _extractor, _sources),
            new DetectFacesHandler(
                _reader, _store, new NeverScanned(), new SqliteFaceRepository(_db),
                models ?? new NoModels()),
            Waiting(),
            new BuildAlbumsHandler(
                new SqliteAlbumRepository(_db), new SqliteAlbumFactsReader(_db)),
            new ShareRenditionsHandler(
                _index,
                new SqliteDecisionReader(_db),
                new SqliteDecisionRepository(_db),
                pool ??= new SharedFolderPool(_index, _workingFolder),
                _store,
                Waiting(),
                models ?? new NoModels(),
                new SharedFolderExchange(_index),

                // The folder the phases copy through is the one asked whether
                // it is there.
                new SyncFolderProbe(_index, pool)
                {
                    Patience = TimeSpan.FromMilliseconds(200),
                }));

    /// <summary>
    /// The real sweep over held answers, not a stub.
    /// </summary>
    /// <remarks>
    /// A library nobody shares with holds none, so every test here exercises the
    /// case that matters most for this phase: that it costs a scan nothing and
    /// changes nothing when there is nothing waiting.
    /// </remarks>
    private ApplyHeldDecisionsHandler Waiting()
    {
        var decisions = new SqliteDecisionReader(_db);
        var writing = new SqliteDecisionRepository(_db);

        return new ApplyHeldDecisionsHandler(
            _index,
            decisions,
            writing,
            new MergedTurns(
                decisions, writing, new NoRenditions(), new SqliteFaceRepository(_db)));
    }

    /// <summary>Renditions these tests never make, so none can be turned.</summary>
    private sealed class NoRenditions : IRenditionTurner
    {
        public TurnedRendition? Turn(string thumbnailName, int degrees) => null;

        public int? DegreesBehindOriginal(
            string thumbnailName, string originalPath, int rotation) => 0;
    }

    /// <summary>
    /// A camera that recorded no position, which is five photographs in six.
    /// </summary>
    private sealed class NoCoordinates : IOriginalCoordinates
    {
        public CoordinateReading Read(string fullPath) => CoordinateReading.None;
    }

    /// <summary>A gazetteer that knows nowhere, so nothing is ever named.</summary>
    private sealed class NowhereNamed : IGeocoder
    {
        public GazetteerPlace? Resolve(double latitude, double longitude) => null;
    }

    /// <summary>
    /// Stands in for the face detector, which nothing here should reach.
    /// </summary>
    /// <remarks>
    /// The face phase is gated on the models being installed and these tests run
    /// with <see cref="NoModels"/>, so it answers "not installed" and returns
    /// before it asks anything of a scanner. Throwing is how that stays true: if
    /// the gate is ever removed by accident, a test fails rather than a pass
    /// quietly reading every preview in the library.
    /// </remarks>
    private sealed class NeverScanned : IFaceScanner
    {
        public Task<IReadOnlyList<ScannedFace>?> ScanAsync(
            string previewPath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The refresh looked for faces with no models installed.");
    }

    /// <summary>
    /// Stands in for the video decoder.
    /// </summary>
    /// <remarks>
    /// Says the clip could not be reached unless a test asks otherwise, which is
    /// the one answer the pass writes nothing down for - so every test in here
    /// about photographs is unaffected by the phase now running after them.
    /// </remarks>
    private sealed class StubExtractor : IKeyframeExtractor
    {
        private int _opened;

        /// <summary>How many clips were opened.</summary>
        public int Opened => _opened;

        /// <summary>Whether this decoder can read anything at all.</summary>
        public bool Decodes { get; set; }

        public Task<KeyframeReading> ExtractAsync(
            string originalPath, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _opened);

            return Task.FromResult(Decodes
                ? KeyframeReading.From(new ExtractedVideo(
                    TimeSpan.FromMinutes(2),
                    1920,
                    1080,
                    [new ExtractedKeyframe(TimeSpan.Zero, [1, 2, 3], [4, 5, 6])]))
                : KeyframeReading.Unavailable);
        }
    }

    /// <summary>Records how often anything wanted to know a model's state.</summary>
    private sealed class CountsWhatItIsAsked : IModelStore
    {
        private readonly List<ModelId> _asked = [];

        public int Asked => _asked.Count;

        /// <summary>
        /// How often the search models in particular were asked about.
        /// </summary>
        /// <remarks>
        /// Counted separately since the face phase joined the scan and shares
        /// this store: it asks about its own weights whenever there are faces
        /// still to look for, which is a different question and a legitimate
        /// one. What must stay at nought is the describing pass asking when it
        /// has nothing to describe.
        /// </remarks>
        public int AskedAboutSearch => _asked.Count(id => id
            is ModelId.ContentVision or ModelId.ContentText
            or ModelId.ContentVocabulary or ModelId.ContentMerges);

        public ModelDescriptor Describe(ModelId id) => ModelManifest.Default.For(id);

        public string ResolvePath(ModelId id) => string.Empty;

        public ModelState StateOf(ModelId id)
        {
            _asked.Add(id);
            return ModelState.Missing;
        }

        public Task<ModelState> ImportAsync(
            ModelId id, string sourcePath, CancellationToken token = default) =>
            Task.FromResult(ModelState.Missing);
    }

    /// <summary>
    /// Stands in for the encoder where the search models are installed, and
    /// describes every picture it is shown.
    /// </summary>
    private sealed class DescribesEverything : IContentEncoder
    {
        public Task<ContentEmbedding?> DescribePictureAsync(
            string previewPath, CancellationToken token = default) =>
            Task.FromResult<ContentEmbedding?>(SomeVector());

        public Task<ContentEmbedding?> DescribePhraseAsync(
            string phrase, CancellationToken token = default) =>
            Task.FromResult<ContentEmbedding?>(SomeVector());
    }

    /// <summary>Stands in for the encoder, which nothing here should reach.</summary>
    private sealed class NeverAsked : IContentEncoder
    {
        public Task<ContentEmbedding?> DescribePictureAsync(
            string previewPath, CancellationToken token = default) =>
            throw new InvalidOperationException(
                "The refresh asked for a description with no models installed.");

        public Task<ContentEmbedding?> DescribePhraseAsync(
            string phrase, CancellationToken token = default) =>
            throw new InvalidOperationException(
                "The refresh asked for a description with no models installed.");
    }

    /// <summary>A machine that has never downloaded the weights.</summary>
    private sealed class NoModels : IModelStore
    {
        public ModelDescriptor Describe(ModelId id) => ModelManifest.Default.For(id);

        public string ResolvePath(ModelId id) => string.Empty;

        public ModelState StateOf(ModelId id) => ModelState.Missing;

        public Task<ModelState> ImportAsync(
            ModelId id, string sourcePath, CancellationToken token = default) =>
            Task.FromResult(ModelState.Missing);
    }

    private Task<PhotoSource> AddSourceAsync() =>
        new AddPhotoSourceHandler(_index, _workingFolder).HandleAsync(_photosRoot);

    /// <summary>Nominates a sync folder for this library, as the Sync screen does.</summary>
    private async Task<string> SyncThroughAsync()
    {
        string shared = Path.Combine(_tempRoot, "shared");
        Directory.CreateDirectory(shared);

        LibrarySettings settings = await _index.GetSettingsAsync();
        settings.SharedFolder = shared;
        await _index.SaveSettingsAsync(settings);

        return shared;
    }

    /// <summary>
    /// Another computer that has prepared the same file this library's crawl
    /// will find, and left its small copy in the sync folder.
    /// </summary>
    private async Task PreparedOnAsync(Library other, string relativePath, string thumbnailName)
    {
        var file = new FileInfo(Path.Combine(_photosRoot, relativePath));

        Asset prepared = other.Prepared(relativePath, thumbnailName);
        Asset theirs = other.Db.Assets.Single(asset => asset.Id == prepared.Id);
        theirs.Length = file.Length;
        theirs.ModifiedUtc = file.LastWriteTimeUtc;
        other.Db.SaveChanges();
        other.Db.ChangeTracker.Clear();

        await other.Pooling.GiveAsync();
    }

    /// <summary>Writes a file the crawl will index; its extension decides the kind.</summary>
    private void WriteMedia(string relativePath) =>
        File.WriteAllText(Path.Combine(_photosRoot, relativePath), $"bytes of {relativePath}");

    private async Task<List<AssetStatus>> StatusesAsync() =>
        await _db.Assets.AsNoTracking().Select(a => a.Status).ToListAsync();

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }

    /// <summary>
    /// Stands in for the Windows codecs, so the pipeline's own behaviour can be
    /// tested without real image files.
    /// </summary>
    private sealed class StubGenerator : IThumbnailGenerator
    {
        public int Generated;

        public HashSet<string> Undecodable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Action? OnGenerate { get; set; }

        public Task<GeneratedThumbnail?> GenerateAsync(
            string originalPath, int rotation = 0, CancellationToken cancellationToken = default)
        {
            OnGenerate?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Generated);

            if (Undecodable.Contains(Path.GetFileName(originalPath)))
            {
                return Task.FromResult<GeneratedThumbnail?>(null);
            }

            // Distinct per file, as a real content hash is: the store names its
            // files after it, so two photos sharing one would share a rendition.
            string hash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(originalPath)));

            return Task.FromResult<GeneratedThumbnail?>(
                new GeneratedThumbnail([1], [2], 100, 100, null, new PerceptualHash(0), hash));
        }
    }
}
