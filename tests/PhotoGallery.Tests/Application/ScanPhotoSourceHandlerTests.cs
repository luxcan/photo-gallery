using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

public sealed class ScanPhotoSourceHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _workingFolderRoot;
    private readonly string _photosRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;
    private readonly SqliteAssetRepository _assets;
    private readonly WorkingFolder _workingFolder;
    private readonly MediaFileWalker _walker;
    private readonly FileSystemThumbnailStore _thumbnails;

    public ScanPhotoSourceHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-scan-{Guid.NewGuid():N}");
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
        _walker = new MediaFileWalker(_workingFolder);
        _thumbnails = new FileSystemThumbnailStore(_workingFolder);
    }

    private ScanPhotoSourceHandler NewHandler() =>
        new(_index,
            _assets,
            _walker,
            _thumbnails,

            // The real ones. A scan parks what was decided about a photograph
            // before its row goes, and these tests are where that has to be
            // true - they are the ones that make files vanish.
            new SqliteDecisionReader(_db),
            new SqliteDecisionRepository(_db));

    private async Task<PhotoSource> AddSourceAsync(string path) =>
        await new AddPhotoSourceHandler(_index, _workingFolder).HandleAsync(path);

    private string WritePhoto(string relativePath, string content = "photo")
    {
        string full = Path.Combine(_photosRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public async Task Scan_IndexesPhotosAndVideosButNotOtherFiles()
    {
        WritePhoto("a.jpg");
        WritePhoto(@"2016\b.HEIC");
        WritePhoto(@"2016\clip.mp4");
        WritePhoto("notes.txt");
        WritePhoto("Thumbs.db");
        PhotoSource source = await AddSourceAsync(_photosRoot);

        ScanResult result = await NewHandler().HandleAsync(source.Id);

        Assert.Equal(3, result.Added);
        Assert.Equal(3, await _assets.CountAsync(source.Id));
    }

    [Fact]
    public async Task Scan_SecondRunSkipsEverythingUnchanged()
    {
        WritePhoto("a.jpg");
        WritePhoto("b.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);

        // The optimisation that makes re-scanning cheap: unchanged files are
        // recognised from size and timestamp alone and never opened.
        ScanResult second = await NewHandler().HandleAsync(source.Id);

        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, second.Unchanged);
        Assert.False(second.ChangedAnything);
    }

    [Fact]
    public async Task Scan_RecordsWhenTheFileWasCreatedAndWhenItWasIndexed()
    {
        WritePhoto("a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);

        await NewHandler().HandleAsync(source.Id);

        Asset asset = await _db.Assets.AsNoTracking().SingleAsync();
        Assert.NotEqual(default, asset.CreatedUtc);
        Assert.NotEqual(default, asset.IndexedUtc);
    }

    [Fact]
    public async Task Scan_FillsInAMissingCreationDateWithoutDiscardingDerivedData()
    {
        // Rows indexed before creation dates were recorded hold none. They have
        // not changed, so stamping them must not put them through the update
        // path - that would throw away the thumbnail, capture date and hashes
        // that cost an hour of reading to produce.
        WritePhoto("a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);

        await _db.Assets.ExecuteUpdateAsync(setters => setters
            .SetProperty(a => a.CreatedUtc, default(DateTime))
            .SetProperty(a => a.ThumbnailName, "abc123.jpg")
            .SetProperty(a => a.TakenUtc, new DateTime(2014, 3, 11)));
        _db.ChangeTracker.Clear();

        ScanResult again = await NewHandler().HandleAsync(source.Id);

        Asset asset = await _db.Assets.AsNoTracking().SingleAsync();
        Assert.Equal(1, again.Unchanged);
        Assert.Equal(0, again.Updated);
        Assert.NotEqual(default, asset.CreatedUtc);
        Assert.Equal("abc123.jpg", asset.ThumbnailName);
        Assert.Equal(new DateTime(2014, 3, 11), asset.TakenUtc);
    }

    [Fact]
    public async Task Scan_LeavesAKnownCreationDateAlone()
    {
        WritePhoto("a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);
        DateTime recorded = (await _db.Assets.AsNoTracking().SingleAsync()).CreatedUtc;
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync(source.Id);

        Assert.Equal(recorded, (await _db.Assets.AsNoTracking().SingleAsync()).CreatedUtc);
    }

    [Fact]
    public async Task Scan_NoticesAnEditedFile()
    {
        string path = WritePhoto("a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);

        File.WriteAllText(path, "a much longer photo body");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        ScanResult second = await NewHandler().HandleAsync(source.Id);

        Assert.Equal(1, second.Updated);
        Assert.Equal(0, second.Added);
    }

    /// <summary>
    /// A replaced file loses its coordinates and its place along with everything
    /// else derived from its bytes.
    /// </summary>
    /// <remarks>
    /// This is the only thing in the app that clears a place, and it has to be:
    /// the coordinates recorded describe a photograph that is no longer there,
    /// so the name resolved from them is no better than they are. Keeping the
    /// place would leave a picture of somewhere else labelled with the old one.
    /// </remarks>
    [Fact]
    public async Task Scan_ForgetsWhereAReplacedFileWasTaken()
    {
        string path = WritePhoto("a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);

        await _db.Assets.ExecuteUpdateAsync(setters => setters
            .SetProperty(a => a.Latitude, 3.4239)
            .SetProperty(a => a.Longitude, 101.7930)
            .SetProperty(a => a.PlaceId, 42));
        _db.ChangeTracker.Clear();

        File.WriteAllText(path, "an entirely different photograph");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        ScanResult second = await NewHandler().HandleAsync(source.Id);

        Assert.Equal(1, second.Updated);

        Asset asset = await _db.Assets.AsNoTracking().SingleAsync();
        Assert.Null(asset.Latitude);
        Assert.Null(asset.Longitude);
        Assert.Null(asset.PlaceId);
    }

    [Fact]
    public async Task Scan_RemovesAssetsWhoseFilesAreGone()
    {
        string path = WritePhoto("a.jpg");
        WritePhoto("b.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);

        File.Delete(path);
        ScanResult second = await NewHandler().HandleAsync(source.Id);

        Assert.Equal(1, second.Removed);
        Assert.Equal(1, await _assets.CountAsync(source.Id));
    }

    [Fact]
    public async Task Scan_SkipsTheAppsOwnFoldersInsideTheWorkingFolder()
    {
        // The working folder doubles as a photo source when set-up points at a
        // pictures folder, so the app's own data must not be indexed.
        File.WriteAllText(Path.Combine(_workingFolder.ThumbnailsPath, "cached.jpg"), "x");
        File.WriteAllText(Path.Combine(_workingFolderRoot, "family.jpg"), "x");
        PhotoSource source = await AddSourceAsync(_workingFolderRoot);

        ScanResult result = await NewHandler().HandleAsync(source.Id);

        Assert.Equal(1, result.Added);
    }

    [Fact]
    public async Task Scan_RecordsWhenTheSourceWasLastScanned()
    {
        WritePhoto("a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        Assert.Null(source.LastScanUtc);

        await NewHandler().HandleAsync(source.Id);

        IReadOnlyList<PhotoSource> reloaded = await _index.GetSourcesAsync();
        Assert.NotNull(reloaded.Single().LastScanUtc);
    }

    [Fact]
    public async Task Scan_ReportsProgress()
    {
        for (int i = 0; i < 5; i++)
        {
            WritePhoto($"p{i}.jpg");
        }

        PhotoSource source = await AddSourceAsync(_photosRoot);
        var reports = new List<ScanProgress>();

        await NewHandler().HandleAsync(source.Id, new Progress<ScanProgress>(reports.Add));

        // Progress<T> marshals asynchronously, so allow the final report to land.
        await Task.Delay(200);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task Scan_CancelledPartWayRemovesNothing()
    {
        WritePhoto("a.jpg");
        WritePhoto("b.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        ScanResult result = await NewHandler().HandleAsync(source.Id, null, cancelled.Token);

        // A cancelled walk has not proved any file missing.
        Assert.True(result.WasCancelled);
        Assert.Equal(0, result.Removed);
        Assert.Equal(2, await _assets.CountAsync(source.Id));
    }

    [Fact]
    public async Task Scan_UnknownSourceThrows() =>
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewHandler().HandleAsync(photoSourceId: 999));

    [Fact]
    public async Task Scan_OfAnUnreachableFolderKeepsEverythingItHadIndexed()
    {
        // What an unplugged drive or an offline share looks like from here. The
        // walk comes back empty either way, and reading that as "every file has
        // gone" once emptied a whole library's index.
        WritePhoto("a.jpg");
        WritePhoto(@"2016\b.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);

        Directory.Move(_photosRoot, _photosRoot + "-away");
        ScanResult second = await NewHandler().HandleAsync(source.Id);

        Assert.True(second.WasUnavailable);
        Assert.Equal(0, second.Removed);
        Assert.Equal(2, second.Kept);
        Assert.Equal(2, await _assets.CountAsync(source.Id));
        Assert.Contains("not reachable", second.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_OfAnUnreachableFolderIsNotRecordedAsScanned()
    {
        WritePhoto("a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);
        DateTime? first = (await _index.GetSourcesAsync()).Single().LastScanUtc;

        Directory.Move(_photosRoot, _photosRoot + "-away");
        await NewHandler().HandleAsync(source.Id);

        // Nothing was established, so the row must not claim it was checked.
        Assert.Equal(first, (await _index.GetSourcesAsync()).Single().LastScanUtc);
    }

    [Fact]
    public async Task Scan_MarksNewPicturesPendingAndVideosSkipped()
    {
        WritePhoto("a.jpg");
        WritePhoto("clip.mp4");
        PhotoSource source = await AddSourceAsync(_photosRoot);

        await NewHandler().HandleAsync(source.Id);

        Assert.Equal(AssetStatus.Pending, await StatusOf("a.jpg"));

        // A video has no rendition to make, so counting it as pending would leave
        // it outstanding for ever.
        Assert.Equal(AssetStatus.Skipped, await StatusOf("clip.mp4"));
    }

    [Fact]
    public async Task Scan_DeletesTheRenditionOfAPictureThatChanged()
    {
        // Renditions are named after the picture's content, so edited bytes are
        // written under a new name and the old pair would stay on disk with
        // nothing naming them.
        string path = WritePhoto("a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);
        string name = await PrepareAsync("a.jpg", "first");

        File.WriteAllText(path, "a much longer photo body");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        ScanResult second = await NewHandler().HandleAsync(source.Id);

        Assert.Equal(1, second.Updated);
        Assert.False(File.Exists(_thumbnails.ResolveTilePath(name)));
        Assert.False(File.Exists(_thumbnails.ResolvePreviewPath(name)));
        Assert.Equal(AssetStatus.Pending, await StatusOf("a.jpg"));
    }

    [Fact]
    public async Task Scan_KeepsARenditionAnotherPictureStillShares()
    {
        // Two identical pictures point at one pair of files. Editing one must not
        // blind the other.
        string path = WritePhoto("a.jpg");
        WritePhoto("copy-of-a.jpg");
        PhotoSource source = await AddSourceAsync(_photosRoot);
        await NewHandler().HandleAsync(source.Id);
        string shared = await PrepareAsync("a.jpg", "shared");
        await PointAtAsync("copy-of-a.jpg", shared);

        File.WriteAllText(path, "a much longer photo body");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        await NewHandler().HandleAsync(source.Id);

        Assert.True(_thumbnails.Exists(shared));
    }

    private async Task<AssetStatus> StatusOf(string relativePath) =>
        await _db.Assets.AsNoTracking()
            .Where(a => a.RelativePath == relativePath)
            .Select(a => a.Status)
            .SingleAsync();

    /// <summary>Stands in for the generating pass having already run on one file.</summary>
    private async Task<string> PrepareAsync(string relativePath, string seed)
    {
        string name = await _thumbnails.SaveAsync(
            new GeneratedThumbnail([1], [2], 100, 100, null, new PerceptualHash(0),
                Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(seed)))));

        await PointAtAsync(relativePath, name);
        return name;
    }

    private async Task PointAtAsync(string relativePath, string thumbnailName)
    {
        await _db.Assets
            .Where(a => a.RelativePath == relativePath)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.ThumbnailName, thumbnailName)
                .SetProperty(a => a.Status, AssetStatus.Ready));
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
        }
    }
}
