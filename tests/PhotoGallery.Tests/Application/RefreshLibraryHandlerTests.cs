using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Collections;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Refresh;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Application.UseCases.Videos;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Search;
using PhotoGallery.Infrastructure.Models;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

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

    /// <summary>Any unit vector; what it points at is beside the point here.</summary>
    private static ContentEmbedding SomeVector()
    {
        float[] values = new float[ContentEmbedding.Dimensions];
        values[0] = 1f;
        return new ContentEmbedding(values);
    }

    private RefreshLibraryHandler NewHandler(IModelStore? models = null) =>
        new(new ScanPhotoSourceHandler(_index, _assets, _walker, _store),
            new BuildThumbnailsHandler(_reader, _assets, _store, _generator),
            new IndexContentHandler(
                _reader,
                _store,
                new NeverAsked(),
                new SqliteContentRepository(_db),
                models ?? new NoModels()),
            new LocatePhotosHandler(
                _reader,
                new NoCoordinates(),
                new NowhereNamed(),
                new SqlitePlaceRepository(_db),
                _assets,
                new EverythingReachable()),
            new BuildVideoKeyframesHandler(_reader, _assets, _store, _extractor),
            new DetectFacesHandler(
                _reader, _store, new NeverScanned(), new SqliteFaceRepository(_db),
                models ?? new NoModels()),
            new BuildCollectionsHandler(
                new SqliteCollectionRepository(_db), new SqliteCollectionFactsReader(_db)));

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

    private sealed class EverythingReachable : ISourceAvailability
    {
        public bool CanReach(string sourceRoot) => true;
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
