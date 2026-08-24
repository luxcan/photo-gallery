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

    private BuildThumbnailsHandler NewHandler() =>
        new(_reader, _assets, _store, _generator);

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
    /// Stands in for the Windows codecs, so the handler's own behaviour - what it
    /// selects, what it records, what it does when interrupted - can be tested
    /// without real image files.
    /// </summary>
    private sealed class StubGenerator : IThumbnailGenerator
    {
        public int Generated;

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
        private static string ContentHashFor(string originalPath) =>
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(originalPath)));

        /// <summary>The turn each call was asked for, so the pass can be checked.</summary>
        public List<int> Rotations { get; } = [];

        public Task<GeneratedThumbnail?> GenerateAsync(
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
                return Task.FromResult<GeneratedThumbnail?>(null);
            }


            return Task.FromResult<GeneratedThumbnail?>(
                new GeneratedThumbnail(
                    [1],
                    [2],
                    1600,
                    1200,
                    TakenUtc,
                    Hash,
                    ContentHashFor(originalPath),
                    Latitude,
                    Longitude));
        }
    }
}
