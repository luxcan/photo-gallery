using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Imaging;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Sharing;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// A turned picture keeps the names on its faces through the next face pass.
/// </summary>
/// <remarks>
/// Through the real turner, store and repositories, because the fault lived in
/// the gap between them: turning rewrites the preview on disk, and the face pass
/// reads a preview written after its last look as a changed picture - so it
/// looked again, and looking again deletes the faces and every name on them.
/// The turn had already moved the boxes, so there was nothing to find again.
/// </remarks>
public sealed class TurnKeepsNamesTests : IDisposable
{
    private const string Name = "aabbccddeeff00112233445566778899.jpg";

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly FileSystemThumbnailStore _store;
    private readonly CountingScanner _scanner = new();

    public TurnKeepsNamesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-turnnames-{Guid.NewGuid():N}");
        var working = new WorkingFolder(Path.Combine(_root, "library"));
        working.EnsureCreated();
        _store = new FileSystemThumbnailStore(working);

        _db = new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
            .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = Path.Combine(_root, "photos") });
        _db.SaveChanges();
    }

    [Theory]
    [InlineData(AssetKind.Photo)]
    [InlineData(AssetKind.Video)]
    public async Task ANameSurvivesTheFacePassAfterATurn(AssetKind kind)
    {
        int assetId = AddPrepared(kind, lookedAt: Yesterday);
        int faceId = AddFace(assetId);
        int personId = NameOn(faceId);

        Assert.True((await Turner().HandleAsync(Name, 90)).Turned);
        FaceDetectionResult pass = await Detector().HandleAsync();

        FaceAssignment kept = Assert.Single(await _db.FaceAssignments.AsNoTracking().ToListAsync());
        Assert.Equal(personId, kept.PersonId);
        Assert.Equal(faceId, kept.FaceId);
        Assert.Equal(0, pass.Pending);
        Assert.Empty(_scanner.Reads);
    }

    [Fact]
    public async Task APictureWithNoFacesIsNotLookedAtAgainAfterATurn()
    {
        // The repository used to return before anything else when there were no
        // boxes to move, so this one had nothing to protect it at all.
        AddPrepared(AssetKind.Photo, lookedAt: Yesterday);

        await Turner().HandleAsync(Name, 180);

        Assert.Equal(0, (await Detector().HandleAsync()).Pending);
    }

    [Fact]
    public async Task APictureNeverLookedAtIsStillLookedAtAfterATurn()
    {
        // The stamp is a record of having looked. Writing it onto a row nobody
        // has looked at would hide that picture's faces from the pass for good.
        int assetId = AddPrepared(AssetKind.Photo, lookedAt: null);

        await Turner().HandleAsync(Name, 90);

        Assert.Null(await _db.Assets.AsNoTracking()
            .Where(asset => asset.Id == assetId)
            .Select(asset => asset.FacesDetectedUtc)
            .SingleAsync());
        Assert.Equal(1, (await Detector().HandleAsync()).Pending);
    }

    private static readonly DateTime Yesterday = DateTime.UtcNow.AddDays(-1);

    private TurnPhotoHandler Turner() => new(
        new WindowsRenditionTurner(_store),
        new ExifOriginalOrientation(),
        new SqliteAssetRepository(_db),
        new SqliteFaceRepository(_db),
        new FakeSourceAvailability());

    private DetectFacesHandler Detector() =>
        new(new SqliteGalleryReader(_db), _store, _scanner, new SqliteFaceRepository(_db),
            new StubModelStore());

    /// <summary>
    /// A row with both renditions on disk, last written when it was looked at -
    /// the state of every picture once the passes have run.
    /// </summary>
    private int AddPrepared(AssetKind kind, DateTime? lookedAt)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = kind == AssetKind.Video ? "clip.mp4" : "photo.jpg",
            Length = 1,
            ModifiedUtc = Yesterday,
            CreatedUtc = Yesterday,
            IndexedUtc = Yesterday,
            Kind = kind,
            Status = AssetStatus.Ready,
            ThumbnailName = Name,
            FacesDetectedUtc = lookedAt,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        foreach (string path in new[] { _store.ResolvePreviewPath(Name), _store.ResolveTilePath(Name) })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, TestJpeg.Bytes(80, 60));
            File.SetLastWriteTimeUtc(path, Yesterday);
        }

        return asset.Id;
    }

    private int AddFace(int assetId)
    {
        var face = new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(10, 5, 20, 30),
            DetectScore = 0.9f,
            Embedding = TestEmbeddings.At(0),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return face.Id;
    }

    private int NameOn(int faceId)
    {
        var person = new Person { DisplayName = "Ana Lim" };
        _db.Set<Person>().Add(person);
        _db.SaveChanges();

        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = faceId,
            PersonId = person.Id,
            Source = AssignmentSource.Confirmed,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return person.Id;
    }

    /// <summary>Finds a face wherever it looks, and remembers having looked.</summary>
    private sealed class CountingScanner : IFaceScanner
    {
        public List<string> Reads { get; } = [];

        public Task<IReadOnlyList<ScannedFace>?> ScanAsync(
            string previewPath, CancellationToken cancellationToken = default)
        {
            lock (Reads)
            {
                Reads.Add(previewPath);
            }

            float[] values = new float[FaceEmbedding.Dimensions];
            values[1] = 1f;
            return Task.FromResult<IReadOnlyList<ScannedFace>?>(
                [new ScannedFace(new FaceBounds(1, 1, 20, 20), 0.9f, new FaceEmbedding(values))]);
        }
    }

    public void Dispose()
    {
        _db.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
