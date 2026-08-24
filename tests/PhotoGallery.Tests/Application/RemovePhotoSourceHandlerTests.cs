using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

public sealed class RemovePhotoSourceHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _workingFolderRoot;
    private readonly string _photosRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;
    private readonly SqliteAssetRepository _assets;
    private readonly FileSystemThumbnailStore _thumbnails;
    private readonly WorkingFolder _workingFolder;

    public RemovePhotoSourceHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-detach-{Guid.NewGuid():N}");
        _workingFolderRoot = Path.Combine(_tempRoot, "library");
        _photosRoot = Path.Combine(_tempRoot, "photos");
        Directory.CreateDirectory(_workingFolderRoot);
        Directory.CreateDirectory(_photosRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_workingFolderRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();
        _index = new SqliteLibraryIndex(_db);
        _assets = new SqliteAssetRepository(_db);
        _workingFolder = new WorkingFolder(_workingFolderRoot);
        _workingFolder.EnsureCreated();
        _thumbnails = new FileSystemThumbnailStore(_workingFolder);
    }

    [Fact]
    public async Task Remove_DetachesTheSource()
    {
        PhotoSource source = await AddSourceAsync(_photosRoot);

        await NewHandler().HandleAsync(source.Id);

        Assert.Empty(await _index.GetSourcesAsync());
    }

    [Fact]
    public async Task Remove_ReclaimsTheCachedCopiesItLeavesBehind()
    {
        // Detaching used to keep the renditions - about 1.6 GB for this library -
        // so the disk was quietly never given back.
        PhotoSource source = await AddSourceAsync(_photosRoot);
        string name = await AddPreparedAssetAsync(source.Id, "a");

        RemovePhotoSourceResult result = await NewHandler().HandleAsync(source.Id);

        Assert.True(result.WasDetached);
        Assert.Equal(1, result.AssetsRemoved);
        Assert.Equal(1, result.CachedCopiesReclaimed);
        Assert.False(File.Exists(_thumbnails.ResolveTilePath(name)));
        Assert.False(File.Exists(_thumbnails.ResolvePreviewPath(name)));
    }

    [Fact]
    public async Task Remove_KeepsACopyAnotherSourceStillUses()
    {
        // Renditions are named after the picture, so two sources holding the same
        // photo point at one pair of files. Detaching one must not blind the other.
        PhotoSource detached = await AddSourceAsync(_photosRoot);
        string otherRoot = Path.Combine(_tempRoot, "photos-2");
        Directory.CreateDirectory(otherRoot);
        PhotoSource kept = await AddSourceAsync(otherRoot);

        string shared = await _thumbnails.SaveAsync(Thumbnail("shared"));
        AddAsset(detached.Id, "a.jpg", shared);
        AddAsset(kept.Id, "copy-of-a.jpg", shared);

        RemovePhotoSourceResult result = await NewHandler().HandleAsync(detached.Id);

        Assert.True(result.WasDetached);
        Assert.Equal(0, result.CachedCopiesReclaimed);
        Assert.True(_thumbnails.Exists(shared));
    }

    [Fact]
    public async Task Remove_MissingSourceIsNotAnError()
    {
        await NewHandler().HandleAsync(sourceId: 999);
    }

    [Fact]
    public async Task Remove_EmptySourceIsNotAnError()
    {
        // Nothing to delete and nothing to flush: an empty batch must not stop
        // the folder being released.
        PhotoSource source = await AddSourceAsync(_photosRoot);

        RemovePhotoSourceResult result = await NewHandler().HandleAsync(source.Id);

        Assert.True(result.WasDetached);
        Assert.Equal(0, result.AssetsTotal);
        Assert.Empty(await _index.GetSourcesAsync());
    }

    [Fact]
    public async Task Remove_KeepsTheSourceAttachedWhenACopyWillNotDelete()
    {
        // The requirement in one test: nothing is detached until everything it
        // owns is proved off the disk.
        PhotoSource source = await AddSourceAsync(_photosRoot);
        string held = await AddPreparedAssetAsync(source.Id, "held");
        await AddPreparedAssetAsync(source.Id, "free");

        using (File.Open(_thumbnails.ResolveTilePath(held),
                   FileMode.Open, FileAccess.Read, FileShare.None))
        {
            RemovePhotoSourceResult result = await NewHandler().HandleAsync(source.Id);

            Assert.False(result.WasDetached);
            Assert.Equal(1, result.CouldNotDelete);
            Assert.Single(await _index.GetSourcesAsync());
        }
    }

    [Fact]
    public async Task Remove_DeletesEachRecordsFilesBeforeItsRow()
    {
        // The row is the only thing that still names the files, so a record whose
        // files survive must keep it - otherwise they are orphaned for good.
        PhotoSource source = await AddSourceAsync(_photosRoot);
        string held = await AddPreparedAssetAsync(source.Id, "held");
        await AddPreparedAssetAsync(source.Id, "free");

        using (File.Open(_thumbnails.ResolveTilePath(held),
                   FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await NewHandler().HandleAsync(source.Id);

            List<string?> left = await _db.Assets.AsNoTracking()
                .Select(a => a.ThumbnailName).ToListAsync();
            Assert.Equal(new[] { held }, left);
        }
    }

    [Fact]
    public async Task Remove_RetriedAfterTheLockIsGoneFinishes()
    {
        PhotoSource source = await AddSourceAsync(_photosRoot);
        string held = await AddPreparedAssetAsync(source.Id, "held");

        using (File.Open(_thumbnails.ResolveTilePath(held),
                   FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await NewHandler().HandleAsync(source.Id);
        }

        RemovePhotoSourceResult second = await NewHandler().HandleAsync(source.Id);

        Assert.True(second.WasDetached);
        Assert.Empty(await _index.GetSourcesAsync());
        Assert.False(File.Exists(_thumbnails.ResolveTilePath(held)));
    }

    [Fact]
    public async Task Remove_WritesTheLastPartialBatch()
    {
        // Seven records is less than one batch of fifty. A loop that only wrote
        // when a batch filled would leave every one of them behind.
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await AddPreparedAssetsAsync(source.Id, 7);

        RemovePhotoSourceResult result = await NewHandler().HandleAsync(source.Id);

        Assert.True(result.WasDetached);
        Assert.Equal(7, result.AssetsRemoved);
        Assert.Equal(0, await _db.Assets.CountAsync());
    }

    [Fact]
    public async Task Remove_WritesTheRemainderAfterFullBatches()
    {
        // One full batch of fifty and a remainder of three.
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await AddPreparedAssetsAsync(source.Id, 53);

        RemovePhotoSourceResult result = await NewHandler().HandleAsync(source.Id);

        Assert.True(result.WasDetached);
        Assert.Equal(53, result.AssetsRemoved);
        Assert.Equal(0, await _db.Assets.CountAsync());
        Assert.Empty(_thumbnails.ListStoredNames());
    }

    [Fact]
    public async Task Remove_StoppedFinishesTheRecordItIsOnAndKeepsTheFolder()
    {
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await AddPreparedAssetsAsync(source.Id, 60);

        using var cancellation = new CancellationTokenSource();

        // Stopped between batches, which is where the loop asks.
        var progress = new SyncProgress<RemovePhotoSourceProgress>(p =>
        {
            if (p.Done > 0)
            {
                cancellation.Cancel();
            }
        });

        RemovePhotoSourceResult result =
            await NewHandler().HandleAsync(source.Id, progress, cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.False(result.WasDetached);
        Assert.Equal(50, result.AssetsRemoved);
        Assert.Single(await _index.GetSourcesAsync());
        Assert.Equal(10, await _db.Assets.CountAsync());
    }

    [Fact]
    public async Task Remove_StoppedMidBatchStillWritesWhatItFinished()
    {
        // Stopped part way through a batch. Every record whose files have gone
        // must have lost its row too, or nothing could ever name them again.
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await AddPreparedAssetsAsync(source.Id, 40);

        using var cancellation = new CancellationTokenSource();
        int deletes = 0;
        var store = new HookedThumbnailStore(_thumbnails, () =>
        {
            if (++deletes == 12)
            {
                cancellation.Cancel();
            }
        });

        RemovePhotoSourceResult result =
            await new RemovePhotoSourceHandler(_index, _assets, store)
                .HandleAsync(source.Id, progress: null, cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(12, result.AssetsRemoved);
        Assert.Single(await _index.GetSourcesAsync());

        // The twenty-eight that are left still have the files their rows name.
        List<string> left = await _db.Assets.AsNoTracking()
            .Select(a => a.ThumbnailName!).ToListAsync();
        Assert.Equal(28, left.Count);
        Assert.All(left, name => Assert.True(_thumbnails.Exists(name)));
    }

    [Fact]
    public async Task Remove_TakesTheOrphansWithIt()
    {
        // A picture whose bytes changed was re-prepared under a new name, leaving
        // its old pair of files with no row naming them. Nothing else can see
        // those, so a completed detach is the only chance to reclaim them.
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await AddPreparedAssetAsync(source.Id, "a");
        string orphan = await _thumbnails.SaveAsync(Thumbnail("orphan"));

        RemovePhotoSourceResult result = await NewHandler().HandleAsync(source.Id);

        Assert.True(result.WasDetached);
        Assert.False(File.Exists(_thumbnails.ResolveTilePath(orphan)));
        Assert.Empty(_thumbnails.ListStoredNames());
    }

    [Fact]
    public async Task Remove_LeavesOrphansAloneWhileAnotherFolderRemains()
    {
        PhotoSource detached = await AddSourceAsync(_photosRoot);
        string otherRoot = Path.Combine(_tempRoot, "photos-2");
        Directory.CreateDirectory(otherRoot);
        PhotoSource kept = await AddSourceAsync(otherRoot);
        await AddPreparedAssetAsync(detached.Id, "a");
        string keptName = await AddPreparedAssetAsync(kept.Id, "b");

        await NewHandler().HandleAsync(detached.Id);

        Assert.True(_thumbnails.Exists(keptName));
    }

    [Fact]
    public async Task Remove_TidiesUpTheDirectoriesItEmpties()
    {
        // Otherwise the thumbs folder still looks full in Explorer after a detach
        // that emptied every one of its shards.
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await AddPreparedAssetsAsync(source.Id, 5);

        await NewHandler().HandleAsync(source.Id);

        Assert.Empty(Directory.GetDirectories(_workingFolder.ThumbnailsPath));
        Assert.True(Directory.Exists(_workingFolder.ThumbnailsPath));
    }

    [Fact]
    public async Task Remove_ReportsProgress()
    {
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await AddPreparedAssetsAsync(source.Id, 3);
        var reports = new List<RemovePhotoSourceProgress>();

        await NewHandler().HandleAsync(
            source.Id, new SyncProgress<RemovePhotoSourceProgress>(reports.Add));

        // One before any work, so the bar paints at nought, and one at the end.
        Assert.Contains(reports, r => r.Done == 0 && r.Total == 3);
        Assert.Contains(reports, r => r.Done == 3 && r.Fraction == 1d);
    }

    private RemovePhotoSourceHandler NewHandler() => new(_index, _assets, _thumbnails);

    private Task<PhotoSource> AddSourceAsync(string path) =>
        new AddPhotoSourceHandler(_index, _workingFolder).HandleAsync(path);

    private async Task<string> AddPreparedAssetAsync(int sourceId, string seed)
    {
        string name = await _thumbnails.SaveAsync(Thumbnail(seed));
        AddAsset(sourceId, $"{seed}.jpg", name);
        return name;
    }

    private async Task AddPreparedAssetsAsync(int sourceId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await AddPreparedAssetAsync(sourceId, $"photo-{i}");
        }
    }

    private void AddAsset(int sourceId, string relativePath, string thumbnailName)
    {
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = sourceId,
            RelativePath = relativePath,
            Length = 1,
            ModifiedUtc = DateTime.UtcNow,
            IndexedUtc = DateTime.UtcNow,
            Kind = AssetKind.Photo,
            ThumbnailName = thumbnailName,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private static GeneratedThumbnail Thumbnail(string seed) =>
        new([1], [2], 100, 100, null, new PerceptualHash(0),
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(seed))));

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A straggling handle on the temp db is not a test failure.
        }
    }

    /// <summary>
    /// Reports on the calling thread, unlike <see cref="Progress{T}"/>, so a test
    /// that acts on a report knows the handler sees it before the next batch.
    /// </summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public SyncProgress(Action<T> onReport) => _onReport = onReport;

        public void Report(T value) => _onReport(value);
    }

    /// <summary>
    /// The real store with a hook on each delete, so a test can stop a detach
    /// part way through a batch rather than only between batches.
    /// </summary>
    private sealed class HookedThumbnailStore : IThumbnailStore
    {
        private readonly IThumbnailStore _inner;
        private readonly Action _onDelete;

        public HookedThumbnailStore(IThumbnailStore inner, Action onDelete)
        {
            _inner = inner;
            _onDelete = onDelete;
        }

        public Task<string> SaveAsync(
            GeneratedThumbnail thumbnail, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(thumbnail, cancellationToken);

        public string NameFor(string contentHash) => _inner.NameFor(contentHash);

        public string ResolveTilePath(string thumbnailName) =>
            _inner.ResolveTilePath(thumbnailName);

        public string ResolvePreviewPath(string thumbnailName) =>
            _inner.ResolvePreviewPath(thumbnailName);

        public bool Exists(string? thumbnailName) => _inner.Exists(thumbnailName);

        public DateTime? PreviewWrittenUtc(string? thumbnailName) =>
            _inner.PreviewWrittenUtc(thumbnailName);

        public bool TryDelete(string? thumbnailName)
        {
            _onDelete();
            return _inner.TryDelete(thumbnailName);
        }

        public IReadOnlyCollection<string> ListStoredNames() => _inner.ListStoredNames();

        public void RemoveEmptyShards() => _inner.RemoveEmptyShards();
    }
}
