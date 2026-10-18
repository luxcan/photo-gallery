using System.Data.Common;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

public sealed class BuildThumbnailsHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _photosRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteAssetRepository _assets;
    private readonly SqliteGalleryReader _reader;
    private readonly FileSystemThumbnailStore _store;
    private readonly StubGenerator _generator = new();

    public BuildThumbnailsHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-build-{Guid.NewGuid():N}");
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

        _assets = new SqliteAssetRepository(_db);
        _reader = new SqliteGalleryReader(_db);

        var workingFolder = new WorkingFolder(workingFolderRoot);
        workingFolder.EnsureCreated();
        _store = new FileSystemThumbnailStore(workingFolder);

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _photosRoot });
        _db.SaveChanges();
    }

    private BuildThumbnailsHandler NewHandler(IThumbnailStore? store = null) =>
        new(_reader, _assets, store ?? _store, _generator);

    [Fact]
    public async Task Build_ProducesTilesForPhotosThatHaveNone()
    {
        AddPhoto("a.jpg");
        AddPhoto("b.jpg");

        ThumbnailBuildResult result = await NewHandler().HandleAsync();

        Assert.Equal(2, result.Pending);
        Assert.Equal(2, result.Built);
        Assert.Equal(0, result.Failed);
        Assert.All(await PhotosAsync(), a => Assert.True(_store.Exists(a.ThumbnailName)));
    }

    [Fact]
    public async Task Build_SkipsPhotosWhoseTileIsAlreadyOnDisk()
    {
        AddPhoto("a.jpg");
        await NewHandler().HandleAsync();
        _generator.Generated = 0;

        ThumbnailBuildResult second = await NewHandler().HandleAsync();

        Assert.Equal(0, second.Pending);
        Assert.Equal(0, _generator.Generated);
    }

    [Fact]
    public async Task Build_RedoesPhotosWhoseTileHasGone()
    {
        // The state a copied or cleaned working folder leaves behind: the row
        // still names a tile, but the file is not there. Judging by the column
        // alone would find nothing to do.
        AddPhoto("a.jpg");
        await NewHandler().HandleAsync();

        Asset photo = (await PhotosAsync()).Single();
        File.Delete(_store.ResolveTilePath(photo.ThumbnailName!));
        _generator.Generated = 0;

        ThumbnailBuildResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Pending);
        Assert.Equal(1, _generator.Generated);
    }

    [Fact]
    public async Task Build_RecordsTheCaptureDateAndTheHash()
    {
        AddPhoto("a.jpg");
        _generator.TakenUtc = new DateTime(2014, 3, 11, 14, 22, 7);
        _generator.Hash = new PerceptualHash(0xAEC3897E81D03EC3);

        await NewHandler().HandleAsync();

        Asset photo = (await PhotosAsync()).Single();
        Assert.Equal(_generator.TakenUtc, photo.TakenUtc);
        Assert.Equal(_generator.Hash, photo.PerceptualHash);
        Assert.Equal(1600, photo.Width);
        Assert.Equal(1200, photo.Height);
    }

    [Fact]
    public async Task Build_RecordsWhereThePhotographWasTaken()
    {
        AddPhoto("a.jpg");
        _generator.Latitude = 3.4239;
        _generator.Longitude = 101.7930;

        await NewHandler().HandleAsync();

        Asset photo = (await PhotosAsync()).Single();
        Assert.Equal(3.4239, photo.Latitude);
        Assert.Equal(101.7930, photo.Longitude);
    }

    [Fact]
    public async Task Build_LeavesAPhotographWithNoCoordinatesUnplaced()
    {
        // 61% of this library. Absent is a fact rather than a failure, and it
        // must not become a zero that later gets named.
        AddPhoto("a.jpg");

        await NewHandler().HandleAsync();

        Asset photo = (await PhotosAsync()).Single();
        Assert.Null(photo.Latitude);
        Assert.Null(photo.Longitude);
    }

    /// <summary>
    /// Preparing a photograph again must not cost it the place it was resolved
    /// to.
    /// </summary>
    /// <remarks>
    /// The whole reason the place is filled by a separate pass and cleared only
    /// by the scan. Renditions get rebuilt for reasons that have nothing to do
    /// with the photograph - a cleared cache, a new preview size, a tile deleted
    /// by hand - and each of those would otherwise send every picture in the
    /// library back through the gazetteer.
    /// </remarks>
    [Fact]
    public async Task Build_DoesNotDisturbThePlaceAlreadyResolved()
    {
        AddPhoto("a.jpg");
        _generator.Latitude = 3.4239;
        _generator.Longitude = 101.7930;
        await NewHandler().HandleAsync();

        Asset placed = await _db.Assets.SingleAsync();
        placed.PlaceId = 42;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Its tile goes, so the pass genuinely does the photograph again.
        File.Delete(_store.ResolveTilePath(placed.ThumbnailName!));
        ThumbnailBuildResult again = await NewHandler().HandleAsync();

        Assert.Equal(1, again.Built);
        Assert.Equal(42, (await PhotosAsync()).Single().PlaceId);
    }

    [Fact]
    public async Task Build_NeverLeavesARowWithoutAHash()
    {
        // The pass used to write null over whatever hash a row held. With every
        // tile missing, one press would have wiped 11,481 irreplaceable values
        // and taken the duplicate feature's groundwork with them. Now the hash
        // comes off the same decode as the tile, so a row that goes through the
        // pass always comes out with one.
        AddPhoto("a.jpg");
        var existing = new PerceptualHash(0x1234567890ABCDEF);
        await _assets.UpdateThumbnailsAsync(
            [new ThumbnailUpdate(1, "deadbeef.jpg", 10, 10, existing, null, null)]);

        await NewHandler().HandleAsync();

        Asset photo = (await PhotosAsync()).Single();
        Assert.NotNull(photo.PerceptualHash);
        Assert.NotEqual(default, photo.PerceptualHash!.Value);
    }

    [Fact]
    public async Task Build_CountsAnUnreadableFileWithoutFailing()
    {
        AddPhoto("good.jpg");
        AddPhoto("bad.jpg");
        _generator.Undecodable.Add("bad.jpg");

        ThumbnailBuildResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Built);
        Assert.Equal(1, result.Failed);
    }

    /// <summary>
    /// Byte-identical photographs handed out together are all prepared, and the
    /// pass goes on.
    /// </summary>
    /// <remarks>
    /// Found by giving a library 300 copies of four photographs: identical
    /// files share one name, the pass prepares several files at once, and two
    /// of them writing that name at the same moment threw an IOException that
    /// ended the scan - so a phone backup or a copied folder of duplicates could
    /// cost a whole scan. Each call here is held until every one has arrived, so
    /// their saves start together, and the preview is large enough that every
    /// write is still going when the others begin.
    /// </remarks>
    [Fact]
    public async Task Build_PreparesByteIdenticalCopiesHandedOutTogether()
    {
        const int copies = 8;
        for (int i = 0; i < copies; i++)
        {
            AddPhoto($"copy {i}.jpg");
        }

        _generator.NamesByContent = true;
        _generator.TogetherAt = copies;
        _generator.PreviewLength = 4 * 1024 * 1024;

        ThumbnailBuildResult result = await NewHandler().HandleAsync(degreeOfParallelism: copies);

        Assert.Equal(copies, result.Built);
        Assert.Equal(0, result.Failed);

        List<Asset> photos = await PhotosAsync();
        string name = Assert.Single(photos.Select(p => p.ThumbnailName).Distinct())!;
        Assert.True(_store.Exists(name));
        Assert.Equal(_generator.PreviewLength, new FileInfo(_store.ResolvePreviewPath(name)).Length);
    }

    /// <summary>
    /// A picture something else was holding costs that picture, not the pass, and
    /// is offered again.
    /// </summary>
    /// <remarks>
    /// The one failure to save that is about one picture rather than the place
    /// they are all kept: a virus scanner, the search indexer or a backup holding
    /// that file at the moment it was put in place. Counted as one that would not
    /// decode is, but not recorded as failed: the file is fine and only the copy
    /// of it could not be put down, so the next pass should try once more.
    /// </remarks>
    [Theory]
    [InlineData(0x20)] // ERROR_SHARING_VIOLATION
    [InlineData(0x21)] // ERROR_LOCK_VIOLATION
    public async Task Build_CountsAPictureItCouldNotSaveAndOffersItAgain(int error)
    {
        AddPhoto("good.jpg");
        AddPhoto("stuck.jpg");

        string stuck = StubGenerator.ContentHashFor(Path.Combine(_photosRoot, "stuck.jpg"));
        bool held = true;
        var holding = new RefusingStore(
            _store, picture => held && picture.ContentHash == stuck ? Win32Fault(error) : null);

        ThumbnailBuildResult result = await NewHandler(holding).HandleAsync();

        Assert.Equal(1, result.Built);
        Assert.Equal(1, result.Failed);

        Asset left = (await PhotosAsync()).Single(p => p.RelativePath == "stuck.jpg");
        Assert.Null(left.ThumbnailName);
        Assert.NotEqual(AssetStatus.Failed, left.Status);

        held = false;
        ThumbnailBuildResult again = await NewHandler(holding).HandleAsync();

        Assert.Equal(1, again.Pending);
        Assert.Equal(1, again.Built);
        Assert.True(_store.Exists(_store.NameFor(stuck)));
    }

    /// <summary>
    /// A fault in the library itself still ends the pass, as it did before.
    /// </summary>
    /// <remarks>
    /// Only a failure to save one picture is that picture's problem. The index
    /// refusing a write is the library's, and it must still reach the screen
    /// that says the scan could not finish.
    /// </remarks>
    [Fact]
    public async Task Build_StillEndsWhenTheIndexRefusesTheWrite()
    {
        AddPhoto("a.jpg");
        await _db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER refuse BEFORE UPDATE ON Assets "
            + "BEGIN SELECT RAISE(ABORT, 'the index refused the write'); END;");

        await Assert.ThrowsAnyAsync<DbException>(() => NewHandler().HandleAsync());
    }

    /// <summary>
    /// And so does a store whose drive is not there at all.
    /// </summary>
    /// <remarks>
    /// Every picture would fail to save, and counting each one would read every
    /// original in the library - an hour over the share - to report nothing but
    /// failures. A folder that cannot be found is the library gone, not one
    /// picture's problem.
    /// </remarks>
    [SkippableFact]
    public async Task Build_StillEndsWhenThePicturesCannotBeKeptAnywhere()
    {
        string? absent = AbsentDrive();
        Skip.If(absent is null, "every drive letter is in use on this machine");

        AddPhoto("a.jpg");
        var nowhere = new FileSystemThumbnailStore(new WorkingFolder(Path.Combine(absent!, "library")));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => NewHandler(nowhere).HandleAsync());
    }

    /// <summary>
    /// A store that can take no picture at all ends the pass at the first one,
    /// with the reason it gave.
    /// </summary>
    /// <remarks>
    /// A full disk, write-protected media, or a working folder on a share or a
    /// drive that has dropped away refuses every picture, not one. Counted a
    /// picture at a time, the pass went on to read every original - an hour over
    /// the share for a large library - recorded nothing, and reported the lot as
    /// pictures that could not be read, while the reason was thrown away and the
    /// next scan did it all again. Windows raises every one of these as a plain
    /// IOException, so it is the error code, not the type, that tells them from
    /// a picture something else is holding.
    /// </remarks>
    [Theory]
    [InlineData(0x70)] // ERROR_DISK_FULL
    [InlineData(0x27)] // ERROR_HANDLE_DISK_FULL
    [InlineData(0x13)] // ERROR_WRITE_PROTECT
    [InlineData(0x15)] // ERROR_NOT_READY
    [InlineData(0x35)] // ERROR_BAD_NETPATH
    [InlineData(0x3B)] // ERROR_UNEXP_NET_ERR
    [InlineData(0x40)] // ERROR_NETNAME_DELETED
    [InlineData(0x48F)] // ERROR_DEVICE_NOT_CONNECTED
    public async Task Build_EndsAtTheFirstPictureWhenTheStoreCanTakeNone(int error) =>
        await AssertEndsAtTheFirstPictureAsync(() => Win32Fault(error));

    /// <summary>
    /// And so does a store that refuses to be written at all.
    /// </summary>
    /// <remarks>
    /// Controlled folder access guarding Pictures, or permissions taken from the
    /// working folder, refuse every picture alike, and the message that says so
    /// is the one the user needs to see.
    /// </remarks>
    [Fact]
    public async Task Build_EndsAtTheFirstPictureWhenTheStoreRefusesEveryWrite() =>
        await AssertEndsAtTheFirstPictureAsync(
            () => new UnauthorizedAccessException("Access to the path is denied."));

    [Fact]
    public async Task Build_IgnoresVideos()
    {
        AddPhoto("a.jpg");
        AddAsset("clip.mov", AssetKind.Video);

        ThumbnailBuildResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Pending);
    }

    [Fact]
    public async Task Build_KeepsWhatItFinishedWhenStopped()
    {
        for (int i = 0; i < 5; i++)
        {
            AddPhoto($"{i}.jpg");
        }

        using var cancellation = new CancellationTokenSource();
        _generator.OnGenerate = () => cancellation.Cancel();

        ThumbnailBuildResult result = await NewHandler()
            .HandleAsync(degreeOfParallelism: 1, progress: null, cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.True(result.Built < 5, "the pass was not actually interrupted");
    }

    [Fact]
    public async Task Build_ReportsNothingToDoOnAnEmptyLibrary()
    {
        ThumbnailBuildResult result = await NewHandler().HandleAsync();

        Assert.Equal(0, result.Pending);
        Assert.Equal("every picture is already prepared", result.Summary);
    }

    private void AddPhoto(string relativePath) => AddAsset(relativePath, AssetKind.Photo);

    private void AddAsset(string relativePath, AssetKind kind)
    {
        File.WriteAllText(Path.Combine(_photosRoot, relativePath), "bytes");
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 5,
            ModifiedUtc = DateTime.UtcNow,
            IndexedUtc = DateTime.UtcNow,
            Kind = kind,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private async Task<List<Asset>> PhotosAsync() =>
        await _db.Assets.AsNoTracking().Where(a => a.Kind == AssetKind.Photo).ToListAsync();

    /// <summary>
    /// Every save refused with a fault from <paramref name="refuse"/>: the pass
    /// must end with that fault, word for word, and without reading another
    /// original after the first.
    /// </summary>
    /// <remarks>
    /// A fresh fault for each save rather than one thrown over and over, which
    /// would gather every throw's stack into its trace.
    /// </remarks>
    private async Task AssertEndsAtTheFirstPictureAsync(Func<Exception> refuse)
    {
        // More than one batch's worth, so the pass stopping is not just a batch
        // running out.
        for (int i = 0; i < 25; i++)
        {
            AddPhoto($"{i}.jpg");
        }

        Exception expected = refuse();
        Exception? thrown = await Record.ExceptionAsync(
            () => NewHandler(new RefusingStore(_store, _ => refuse()))
                .HandleAsync(degreeOfParallelism: 1));

        Assert.NotNull(thrown);
        Assert.IsType(expected.GetType(), thrown);
        Assert.Equal((expected.Message, expected.HResult), (thrown.Message, thrown.HResult));
        Assert.Equal(1, _generator.Generated);
    }

    /// <summary>
    /// The IOException .NET raises for a Windows error it has no type of its own
    /// for: the system's own words, with the error as its HResult.
    /// </summary>
    private static IOException Win32Fault(int error) =>
        new(Marshal.GetPInvokeErrorMessage(error), unchecked((int)0x80070000) | error);

    /// <summary>A drive letter nothing on this machine answers to, or null.</summary>
    private static string? AbsentDrive()
    {
        HashSet<string> used = new(
            DriveInfo.GetDrives().Select(drive => drive.Name), StringComparer.OrdinalIgnoreCase);

        return Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(letter => $"{(char)letter}:\\")
            .LastOrDefault(root => !used.Contains(root));
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
            // A temp folder that outlives the test run is not a test failure.
        }
    }

    /// <summary>
    /// The real store, refusing the saves a test picks with the fault it picks.
    /// </summary>
    /// <remarks>
    /// A full disk, a share that has dropped and a file another program is
    /// holding are not things a test can arrange on demand, and what the pass
    /// makes of each is decided by the fault alone. Everything else is passed
    /// straight through, so what a refused picture leaves on disk is the real
    /// store's answer.
    /// </remarks>
    private sealed class RefusingStore : IThumbnailStore
    {
        private readonly IThumbnailStore _inner;
        private readonly Func<GeneratedThumbnail, Exception?> _refusal;

        public RefusingStore(IThumbnailStore inner, Func<GeneratedThumbnail, Exception?> refusal)
        {
            _inner = inner;
            _refusal = refusal;
        }

        public Task<string> SaveAsync(
            GeneratedThumbnail thumbnail, CancellationToken cancellationToken = default) =>
            _refusal(thumbnail) is Exception fault
                ? Task.FromException<string>(fault)
                : _inner.SaveAsync(thumbnail, cancellationToken);

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

    /// <summary>
    /// Stands in for the Windows codecs, so the handler's own behaviour - what it
    /// selects, what it records, what it does when interrupted - can be tested
    /// without real image files.
    /// </summary>
    private sealed class StubGenerator : IThumbnailGenerator
    {
        /// <summary>How long calls held for one another are given to arrive.</summary>
        /// <remarks>
        /// Only ever spent by a failing test: the calls this waits for are made
        /// within moments of each other, or not at all.
        /// </remarks>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly TaskCompletionSource _together =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrived;

        public int Generated;

        /// <summary>
        /// Names each picture after the file's bytes, as the real generator does,
        /// rather than after its path - so byte-identical copies share a name.
        /// </summary>
        public bool NamesByContent { get; set; }

        /// <summary>Holds every call until this many have arrived, when above zero.</summary>
        public int TogetherAt { get; set; }

        /// <summary>How many bytes each preview is.</summary>
        public int PreviewLength { get; set; } = 1;

        public DateTime? TakenUtc { get; set; }

        public PerceptualHash Hash { get; set; } = new(0x0F0F0F0F0F0F0F0F);

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public HashSet<string> Undecodable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Action? OnGenerate { get; set; }

        /// <summary>
        /// Distinct per file, as a real content hash is - the store names its
        /// files after it, so two photos sharing one would share a tile.
        /// </summary>
        public static string ContentHashFor(string originalPath) =>
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(originalPath)));

        /// <summary>The turn each call was asked for, so the pass can be checked.</summary>
        public List<int> Rotations { get; } = [];

        public async Task<GeneratedThumbnail?> GenerateAsync(
            string originalPath, int rotation = 0, CancellationToken cancellationToken = default)
        {
            lock (Rotations)
            {
                Rotations.Add(rotation);
            }

            OnGenerate?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Generated);

            if (Undecodable.Contains(Path.GetFileName(originalPath)))
            {
                return null;
            }

            if (TogetherAt > 0)
            {
                if (Interlocked.Increment(ref _arrived) >= TogetherAt)
                {
                    _together.TrySetResult();
                }

                await _together.Task.WaitAsync(Patience, cancellationToken);
            }

            byte[] preview = new byte[PreviewLength];
            Array.Fill(preview, (byte)2);

            return new GeneratedThumbnail(
                [1],
                preview,
                1600,
                1200,
                TakenUtc,
                Hash,
                NamesByContent
                    ? Convert.ToHexStringLower(
                        System.Security.Cryptography.SHA256.HashData(
                            await File.ReadAllBytesAsync(originalPath, cancellationToken)))
                    : ContentHashFor(originalPath),
                Latitude,
                Longitude);
        }
    }
}
