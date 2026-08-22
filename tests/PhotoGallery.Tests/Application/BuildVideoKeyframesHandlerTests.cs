using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Videos;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The pass that gives videos a poster and the frames the face pass reads.
/// </summary>
/// <remarks>
/// Runs against a real index and a real thumbnail store, with only the decoder
/// faked - the decoder is the one part that needs a codec and a machine, and
/// everything worth pinning here is about what the pass does with what it gets
/// back: what it writes, in what order, and what it does a second time.
/// </remarks>
public sealed class BuildVideoKeyframesHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteAssetRepository _assets;
    private readonly SqliteGalleryReader _reader;
    private readonly FileSystemThumbnailStore _thumbnails;
    private readonly WorkingFolder _workingFolder;
    private readonly int _sourceId;

    public BuildVideoKeyframesHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-video-{Guid.NewGuid():N}");
        string workingFolderRoot = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(workingFolderRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(workingFolderRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();

        _assets = new SqliteAssetRepository(_db);
        _reader = new SqliteGalleryReader(_db);
        _workingFolder = new WorkingFolder(workingFolderRoot);
        _workingFolder.EnsureCreated();
        _thumbnails = new FileSystemThumbnailStore(_workingFolder);

        var source = new PhotoSource { Path = Path.Combine(_tempRoot, "videos") };
        _db.PhotoSources.Add(source);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _sourceId = source.Id;
    }

    [Fact]
    public async Task AVideoGetsAPosterAndItsCompanions()
    {
        AddVideo(@"2023\clip.mp4");
        BuildVideoKeyframesHandler handler = HandlerYielding(frames: 3);

        VideoBuildResult result = await handler.HandleAsync();

        Assert.Equal(1, result.Considered);
        Assert.Equal(1, result.Prepared);
        Assert.Equal(0, result.Failed);

        Asset video = _db.Assets.AsNoTracking().Single();
        Assert.NotNull(video.ThumbnailName);
        Assert.True(_thumbnails.Exists(video.ThumbnailName));
        Assert.Equal(AssetStatus.Ready, video.Status);
    }

    [Fact]
    public async Task EveryFrameIsRecordedAgainstTheVideo()
    {
        AddVideo(@"2023\clip.mp4");

        await HandlerYielding(frames: 3).HandleAsync();

        List<VideoKeyframe> frames = [.. _db.VideoKeyframes.AsNoTracking().OrderBy(k => k.Ordinal)];

        Assert.Equal(3, frames.Count);
        Assert.Equal(new[] { 0, 1, 2 }, frames.Select(f => f.Ordinal));
        Assert.All(frames, frame => Assert.True(_thumbnails.Exists(frame.ThumbnailName)));
    }

    [Fact]
    public async Task ThePosterIsTheFrameTheRowDraws()
    {
        AddVideo(@"2023\clip.mp4");

        await HandlerYielding(frames: 3).HandleAsync();

        Asset video = _db.Assets.AsNoTracking().Single();
        VideoKeyframe poster = _db.VideoKeyframes.AsNoTracking().Single(k => k.Ordinal == 0);

        Assert.Equal(poster.ThumbnailName, video.ThumbnailName);
    }

    [Fact]
    public async Task ThePosterIsWrittenLast()
    {
        AddVideo(@"2023\clip.mp4");

        var written = new List<string>();
        var recording = new RecordingThumbnailStore(_thumbnails, written.Add);
        var handler = new BuildVideoKeyframesHandler(
            _reader, _assets, recording, new FakeExtractor(frames: 3));

        await handler.HandleAsync();

        // The pass decides what is outstanding by looking for the poster, so a
        // poster written first would let a clip interrupted between its frames
        // look finished for ever - with the rest of it never scanned for faces.
        Asset video = _db.Assets.AsNoTracking().Single();
        Assert.Equal(video.ThumbnailName, written[^1]);
    }

    [Fact]
    public async Task AVideoAlreadyDoneIsNotOpenedAgain()
    {
        AddVideo(@"2023\clip.mp4");
        await HandlerYielding(frames: 3).HandleAsync();

        var second = new FakeExtractor(frames: 3);
        var handler = new BuildVideoKeyframesHandler(_reader, _assets, _thumbnails, second);

        VideoBuildResult result = await handler.HandleAsync();

        Assert.Equal(0, result.Considered);
        Assert.Equal(0, second.Opened);
    }

    [Fact]
    public async Task AClipWhoseFileChangedIsDoneAgain()
    {
        AddVideo(@"2023\clip.mp4");
        await HandlerYielding(frames: 3).HandleAsync();

        // The frames on disk describe a clip that is no longer there, and their
        // names were derived from the length that has just changed.
        await _db.Assets.ExecuteUpdateAsync(s => s.SetProperty(a => a.Length, 999_999));

        VideoBuildResult result = await HandlerYielding(frames: 3).HandleAsync();

        Assert.Equal(1, result.Considered);
        Assert.Equal(1, result.Prepared);
    }

    [Fact]
    public async Task AContainerThatWillNotDecodeIsRecordedRatherThanRetried()
    {
        AddVideo(@"2023\broken.mts");
        var handler = new BuildVideoKeyframesHandler(
            _reader, _assets, _thumbnails, new FakeExtractor(frames: 0));

        VideoBuildResult first = await handler.HandleAsync();

        Assert.Equal(1, first.Failed);
        Assert.Equal(AssetStatus.Failed, _db.Assets.AsNoTracking().Single().Status);

        // Recorded as a fact, so it does not cost an open on every future run.
        var second = new FakeExtractor(frames: 3);
        await new BuildVideoKeyframesHandler(_reader, _assets, _thumbnails, second)
            .HandleAsync();

        Assert.Equal(0, second.Opened);
    }

    [Fact]
    public async Task AShareThatBlinkedDoesNotCostAClipItsPoster()
    {
        // The bug this pass shipped with, found on the real library: it wrote
        // off 24 videos in 468, and five of the six checked by hand gave a
        // poster immediately afterwards. A file that could not be reached is not
        // an answer about the file, and recording it as one leaves the clip
        // blank for good the moment the share comes back.
        AddVideo(@"2023\clip.mp4");
        var away = new FakeExtractor(frames: 0) { FailureOutcome = KeyframeOutcome.Unavailable };

        VideoBuildResult first = await new BuildVideoKeyframesHandler(
            _reader, _assets, _thumbnails, away).HandleAsync();

        Assert.Equal(0, first.Failed);
        Assert.Equal(1, first.Skipped);

        // Nothing written down, so the row is exactly where the scan left it.
        Assert.Equal(AssetStatus.Skipped, _db.Assets.AsNoTracking().Single().Status);

        // And the next run offers it again, rather than passing over it forever.
        var back = new FakeExtractor(frames: 1);
        VideoBuildResult second = await new BuildVideoKeyframesHandler(
            _reader, _assets, _thumbnails, back).HandleAsync();

        Assert.Equal(1, back.Opened);
        Assert.Equal(1, second.Prepared);
        Assert.NotNull(_db.Assets.AsNoTracking().Single().ThumbnailName);
    }

    [Fact]
    public async Task AClipWithNoLengthStillGetsItsPoster()
    {
        AddVideo(@"2023\clip.mp4");
        var handler = new BuildVideoKeyframesHandler(
            _reader, _assets, _thumbnails, new FakeExtractor(frames: 1, duration: null));

        VideoBuildResult result = await handler.HandleAsync();

        Assert.Equal(1, result.Prepared);
        Assert.Null(_db.Assets.AsNoTracking().Single().Duration);
        Assert.NotNull(_db.Assets.AsNoTracking().Single().ThumbnailName);
    }

    [Fact]
    public async Task PreparingAClipPutsItBackToTheFacePass()
    {
        AddVideo(@"2023\clip.mp4");
        await _db.Assets.ExecuteUpdateAsync(
            s => s.SetProperty(a => a.FacesDetectedUtc, DateTime.UtcNow));

        await HandlerYielding(frames: 3).HandleAsync();

        // The frames are new files. Whatever was recorded against this clip
        // before describes frames that have gone.
        Assert.Null(_db.Assets.AsNoTracking().Single().FacesDetectedUtc);
    }

    [Fact]
    public async Task PhotographsAreLeftToThePreparingPass()
    {
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = _sourceId,
            RelativePath = @"2023\photo.jpg",
            Length = 10,
            ModifiedUtc = DateTime.UtcNow,
            IndexedUtc = DateTime.UtcNow,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Pending,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var extractor = new FakeExtractor(frames: 3);
        VideoBuildResult result = await new BuildVideoKeyframesHandler(
            _reader, _assets, _thumbnails, extractor).HandleAsync();

        Assert.Equal(0, result.Considered);
        Assert.Equal(0, extractor.Opened);
    }

    [Fact]
    public async Task AQuarantinedCopyIsNotWorthAnHourOfSeeking()
    {
        AddVideo(@"2023\clip.mp4");
        await _db.Assets.ExecuteUpdateAsync(
            s => s.SetProperty(a => a.QuarantinedUtc, DateTime.UtcNow));

        VideoBuildResult result = await HandlerYielding(frames: 3).HandleAsync();

        Assert.Equal(0, result.Considered);
    }

    private BuildVideoKeyframesHandler HandlerYielding(int frames) =>
        new(_reader, _assets, _thumbnails, new FakeExtractor(frames));

    private void AddVideo(string relativePath)
    {
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = _sourceId,
            RelativePath = relativePath,
            Length = 5_000,
            ModifiedUtc = new DateTime(2026, 3, 5, 14, 30, 0, DateTimeKind.Utc),
            IndexedUtc = DateTime.UtcNow,
            Kind = AssetKind.Video,

            // Where the scan parks a video: there is no rendition the preparing
            // pass could make from it.
            Status = AssetStatus.Skipped,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();

        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test. SQLite pools
            // its connections, so the index file can stay open for a moment
            // after the context that used it has gone.
        }
    }

    /// <summary>A decoder that yields whatever the test asked for.</summary>
    private sealed class FakeExtractor : IKeyframeExtractor
    {
        /// <summary>A clip's length when the test has no opinion about it.</summary>
        private static readonly TimeSpan TypicalLength = TimeSpan.FromMinutes(2);

        private readonly int _frames;
        private readonly TimeSpan? _duration;

        public FakeExtractor(int frames)
            : this(frames, TypicalLength)
        {
        }

        /// <summary>
        /// What this decoder says when it yields no frames.
        /// </summary>
        /// <remarks>
        /// Undecodable by default, which is what <c>frames: 0</c> meant before
        /// there was anything else to mean. A test that wants the other failure
        /// - a share that blinked - sets this to Unavailable, and the pass must
        /// then leave the row alone.
        /// </remarks>
        public KeyframeOutcome FailureOutcome { get; set; } = KeyframeOutcome.Undecodable;

        /// <summary>
        /// A decoder whose answer about length is whatever the test says -
        /// including "it did not say", which is what the shell extractor gives
        /// back for every clip. A default parameter cannot express that: it
        /// would make the deliberate null indistinguishable from the one the
        /// other tests leave out.
        /// </summary>
        public FakeExtractor(int frames, TimeSpan? duration) =>
            (_frames, _duration) = (frames, duration);

        /// <summary>How many videos were actually opened.</summary>
        public int Opened => _opened;

        private int _opened;

        public Task<KeyframeReading> ExtractAsync(
            string originalPath, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _opened);

            if (_frames == 0)
            {
                return Task.FromResult(FailureOutcome == KeyframeOutcome.Undecodable
                    ? KeyframeReading.Undecodable
                    : KeyframeReading.Unavailable);
            }

            ExtractedKeyframe[] frames =
            [
                .. Enumerable.Range(0, _frames).Select(i => new ExtractedKeyframe(
                    TimeSpan.FromSeconds(i * 10),
                    [(byte)i, 1],
                    [(byte)i, 2])),
            ];

            return Task.FromResult(
                KeyframeReading.From(new ExtractedVideo(_duration, 1920, 1080, frames)));
        }
    }

    /// <summary>The real store, noting the order renditions are written in.</summary>
    private sealed class RecordingThumbnailStore : IThumbnailStore
    {
        private readonly IThumbnailStore _inner;
        private readonly Action<string> _onSaved;

        public RecordingThumbnailStore(IThumbnailStore inner, Action<string> onSaved) =>
            (_inner, _onSaved) = (inner, onSaved);

        public async Task<string> SaveAsync(
            GeneratedThumbnail thumbnail, CancellationToken cancellationToken = default)
        {
            string name = await _inner.SaveAsync(thumbnail, cancellationToken);
            _onSaved(name);
            return name;
        }

        public string NameFor(string contentHash) => _inner.NameFor(contentHash);

        public string ResolveTilePath(string thumbnailName) =>
            _inner.ResolveTilePath(thumbnailName);

        public string ResolvePreviewPath(string thumbnailName) =>
            _inner.ResolvePreviewPath(thumbnailName);

        public bool Exists(string? thumbnailName) => _inner.Exists(thumbnailName);

        public DateTime? PreviewWrittenUtc(string? thumbnailName) =>
            _inner.PreviewWrittenUtc(thumbnailName);

        public bool TryDelete(string? thumbnailName) => _inner.TryDelete(thumbnailName);

        public IReadOnlyCollection<string> ListStoredNames() => _inner.ListStoredNames();

        public void RemoveEmptyShards() => _inner.RemoveEmptyShards();
    }
}
