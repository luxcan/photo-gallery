using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

public sealed class DetectFacesHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteGalleryReader _reader;
    private readonly SqliteFaceRepository _faces;
    private readonly FileSystemThumbnailStore _store;
    private readonly StubFaceScanner _scanner = new();
    private readonly StubModelStore _models = new();

    public DetectFacesHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-detect-{Guid.NewGuid():N}");
        string workingFolderRoot = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(workingFolderRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(workingFolderRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();

        _reader = new SqliteGalleryReader(_db);
        _faces = new SqliteFaceRepository(_db);

        var workingFolder = new WorkingFolder(workingFolderRoot);
        workingFolder.EnsureCreated();
        _store = new FileSystemThumbnailStore(workingFolder);

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = Path.Combine(_tempRoot, "photos") });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Detect_RecordsWhatItFindsAndMarksThePhotoLookedAt()
    {
        int assetId = AddPhoto("a.jpg", "aa11.jpg");
        _scanner.Faces["aa11.jpg"] = [Face(10, 20, 60, 60, 0.9f), Face(90, 20, 50, 50, 0.8f)];

        FaceDetectionResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Pending);
        Assert.Equal(2, result.FacesFound);
        Assert.Equal(2, await _db.Faces.CountAsync(f => f.AssetId == assetId));
        Assert.NotNull(await MarkerOf(assetId));

        Face stored = await _db.Faces.AsNoTracking().FirstAsync(f => f.AssetId == assetId);
        Assert.Equal(new FaceBounds(10, 20, 60, 60), stored.Bounds);
        Assert.Equal(FaceEmbedding.Dimensions, stored.Embedding.Values.Length);
    }

    [Fact]
    public async Task Detect_MarksAPhotoWithNoFacesSoItIsNeverReadAgain()
    {
        // The whole reason the marker exists. A landscape produces nothing, and
        // without a record of having looked it is indistinguishable from a photo
        // that has never been examined.
        int assetId = AddPhoto("empty.jpg", "bb22.jpg");
        _scanner.Faces["bb22.jpg"] = [];

        await NewHandler().HandleAsync();

        Assert.Empty(await _db.Faces.Where(f => f.AssetId == assetId).ToListAsync());
        Assert.NotNull(await MarkerOf(assetId));

        _scanner.Reads.Clear();
        FaceDetectionResult second = await NewHandler().HandleAsync();

        Assert.Equal(0, second.Pending);
        Assert.Empty(_scanner.Reads);
    }

    [Fact]
    public async Task Detect_ReadsASharedRenditionOnceAndWritesRowsForEveryPhotoUsingIt()
    {
        // Renditions are named after the picture's content, so two byte-identical
        // photos share one file. Reading it twice would be the same work twice.
        int first = AddPhoto("one.jpg", "cc33.jpg");
        int second = AddPhoto("copy.jpg", "cc33.jpg");
        _scanner.Faces["cc33.jpg"] = [Face(5, 5, 40, 40, 0.7f)];

        FaceDetectionResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Pending);
        Assert.Single(_scanner.Reads);
        Assert.Equal(1, await _db.Faces.CountAsync(f => f.AssetId == first));
        Assert.Equal(1, await _db.Faces.CountAsync(f => f.AssetId == second));
    }

    [Fact]
    public async Task Detect_ResumesWhatAStopLeftPending()
    {
        for (int i = 0; i < 40; i++)
        {
            AddPhoto($"photo-{i}.jpg", $"name{i:D2}.jpg");
            _scanner.Faces[$"name{i:D2}.jpg"] = [Face(1, 1, 40, 40, 0.9f)];
        }

        using var cancellation = new CancellationTokenSource();
        int read = 0;
        _scanner.OnScan = () =>
        {
            // Part way through the second batch of twenty.
            if (Interlocked.Increment(ref read) == 25)
            {
                cancellation.Cancel();
            }
        };

        FaceDetectionResult stopped =
            await NewHandler().HandleAsync(degreeOfParallelism: 1, cancellationToken: cancellation.Token);

        Assert.True(stopped.WasCancelled);
        Assert.InRange(stopped.Scanned, 1, 39);

        _scanner.OnScan = null;
        FaceDetectionResult finished = await NewHandler().HandleAsync();

        Assert.Equal(40 - stopped.Scanned, finished.Pending);
        Assert.Equal(40, await _db.Assets.CountAsync(a => a.FacesDetectedUtc != null));
        Assert.Equal(40, await _db.Faces.CountAsync());
    }

    [Fact]
    public async Task Detect_LeavesAPreviewItCouldNotReadToBeTriedAgain()
    {
        // Unlike an original, a preview is this app's own file and the preparing
        // pass can make it again. Writing the photo off for good would mean it
        // stayed faceless even once the rendition came back.
        int assetId = AddPhoto("broken.jpg", "dd44.jpg");
        _scanner.Unreadable.Add("dd44.jpg");

        FaceDetectionResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Scanned);
        Assert.Null(await MarkerOf(assetId));
        Assert.Equal(1, (await NewHandler().HandleAsync()).Pending);
    }

    [Fact]
    public async Task Detect_SkipsAPhotoWhoseRenditionIsNotOnDisk()
    {
        AddPhoto("gone.jpg", "ee55.jpg", writeRendition: false);

        FaceDetectionResult result = await NewHandler().HandleAsync();

        Assert.Equal(0, result.Pending);
        Assert.Empty(_scanner.Reads);
    }

    [Fact]
    public async Task Detect_ReplacesWhatAnEarlierScanRecorded()
    {
        int assetId = AddPhoto("changed.jpg", "ff66.jpg");
        _scanner.Faces["ff66.jpg"] = [Face(1, 1, 40, 40, 0.9f), Face(60, 1, 40, 40, 0.9f)];
        await NewHandler().HandleAsync();

        // What the scan pass does when a file's bytes change.
        await _db.Assets.Where(a => a.Id == assetId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.FacesDetectedUtc, (DateTime?)null));

        _scanner.Faces["ff66.jpg"] = [Face(9, 9, 30, 30, 0.6f)];
        await NewHandler().HandleAsync();

        Assert.Equal(1, await _db.Faces.CountAsync(f => f.AssetId == assetId));
        Assert.Equal(
            new FaceBounds(9, 9, 30, 30),
            (await _db.Faces.AsNoTracking().FirstAsync(f => f.AssetId == assetId)).Bounds);
    }

    [Fact]
    public async Task Detect_WithoutTheModelsDoesNothingAndSaysSo()
    {
        AddPhoto("a.jpg", "aa11.jpg");
        _models.State = ModelState.Missing;

        FaceDetectionResult result = await NewHandler().HandleAsync();

        Assert.True(result.ModelsMissing);
        Assert.Empty(_scanner.Reads);
        Assert.Contains("not installed", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detect_IgnoresVideosAndPhotosThatAreNotPreparedYet()
    {
        AddPhoto("pending.jpg", "aa11.jpg", status: AssetStatus.Pending);
        AddPhoto("failed.jpg", "bb22.jpg", status: AssetStatus.Failed);
        AddPhoto("clip.mov", "cc33.jpg", status: AssetStatus.Skipped, kind: AssetKind.Video);

        Assert.Equal(0, (await NewHandler().HandleAsync()).Pending);
    }

    [Fact]
    public async Task Detect_ReadsAVideosPosterLikeAnyOtherPicture()
    {
        // What [08] means by keyframes feeding the face pipeline unchanged. The
        // frame was written into the same store under the same kind of name, so
        // nothing here has to know it came out of a film.
        int video = AddPhoto("clip.mov", "cc33.jpg", kind: AssetKind.Video);

        FaceDetectionResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Pending);
        Assert.Equal(["cc33.jpg"], _scanner.Reads);
        Assert.NotNull(await MarkerOf(video));
    }

    [Fact]
    public async Task Detect_FindsThePeopleInAVideo()
    {
        int video = AddPhoto("clip.mov", "cc33.jpg", kind: AssetKind.Video);
        _scanner.Faces["cc33.jpg"] = [Face(10, 20, 60, 60, 0.9f)];

        FaceDetectionResult result = await NewHandler().HandleAsync();

        // The point of the whole feature: a face in a film is a face in the
        // library, and is found by the same search that finds it in a photo.
        Assert.Equal(1, result.FacesFound);
        Assert.Equal(1, await _db.Faces.CountAsync(f => f.AssetId == video));
    }

    private DetectFacesHandler NewHandler() =>
        new(_reader, _store, _scanner, _faces, _models);

    private Task<DateTime?> MarkerOf(int assetId) =>
        _db.Assets.AsNoTracking().Where(a => a.Id == assetId)
            .Select(a => a.FacesDetectedUtc).FirstAsync();

    private int AddPhoto(
        string relativePath,
        string thumbnailName,
        bool writeRendition = true,
        AssetStatus status = AssetStatus.Ready,
        AssetKind kind = AssetKind.Photo)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024,
            ModifiedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IndexedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Kind = kind,
            Status = status,
            ThumbnailName = thumbnailName,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        if (writeRendition)
        {
            foreach (string path in new[]
            {
                _store.ResolveTilePath(thumbnailName), _store.ResolvePreviewPath(thumbnailName),
            })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, [1, 2, 3]);
            }
        }

        return asset.Id;
    }

    private static ScannedFace Face(int x, int y, int width, int height, float score)
    {
        float[] values = new float[FaceEmbedding.Dimensions];
        values[0] = 1f;
        return new ScannedFace(new FaceBounds(x, y, width, height), score, new FaceEmbedding(values));
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
            // A temp folder that will not go is not a failed test.
        }
    }

    private sealed class StubModelStore : IModelStore
    {
        public ModelState State { get; set; } = ModelState.Ready;

        public ModelDescriptor Describe(ModelId id) => new(id, 1, "stub.onnx", 0, string.Empty, "test");

        public string ResolvePath(ModelId id) => "stub.onnx";

        public ModelState StateOf(ModelId id) => State;

        public Task<ModelState> ImportAsync(
            ModelId id, string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(State);
    }

    private sealed class StubFaceScanner : IFaceScanner
    {
        public Dictionary<string, ScannedFace[]> Faces { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Reads { get; } = [];

        public Action? OnScan { get; set; }

        public Task<IReadOnlyList<ScannedFace>?> ScanAsync(
            string previewPath, CancellationToken cancellationToken = default)
        {
            OnScan?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            // The preview's name carries the "-p" suffix the store adds.
            string name = Path.GetFileNameWithoutExtension(previewPath);
            name = name.EndsWith("-p", StringComparison.Ordinal) ? name[..^2] : name;
            name += Path.GetExtension(previewPath);

            lock (Reads)
            {
                Reads.Add(name);
            }

            if (Unreadable.Contains(name))
            {
                return Task.FromResult<IReadOnlyList<ScannedFace>?>(null);
            }

            return Task.FromResult<IReadOnlyList<ScannedFace>?>(
                Faces.TryGetValue(name, out ScannedFace[]? found) ? found : []);
        }
    }
}
