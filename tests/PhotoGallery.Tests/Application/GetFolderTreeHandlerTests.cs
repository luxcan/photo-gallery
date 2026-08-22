using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The tree through the real reader, so the query, the source names and the
/// roll-up are exercised together rather than only the pure builder.
/// </summary>
public sealed class GetFolderTreeHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteGalleryReader _reader;
    private int _nextId = 1;

    public GetFolderTreeHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-tree-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();
        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = @"\\nas\PhotoGallery" });
        _db.SaveChanges();

        _reader = new SqliteGalleryReader(_db);
    }

    private GetFolderTreeHandler NewHandler() => new(_reader);

    [Fact]
    public async Task Folders_StartAtThePhotoSource()
    {
        Add(@"20200214_Ana Lim Born\a.jpg");
        Add(@"20230203 - Chingay\b.jpg");

        IReadOnlyList<FolderNode> tree = await NewHandler().HandleAsync();

        FolderNode source = Assert.Single(tree);
        Assert.Equal(@"\\nas\PhotoGallery", source.Name);
        Assert.Equal(string.Empty, source.RelativeFolder);
        Assert.Equal(2, source.ItemCount);
        Assert.Equal(
            ["20200214_Ana Lim Born", "20230203 - Chingay"],
            source.Children.Select(c => c.Name));
    }

    [Fact]
    public async Task Folders_NestOneInsideAnother()
    {
        Add(@"20250419 - Kidzania\a.jpg");
        Add(@"20250419 - Kidzania\signs\b.jpg");

        IReadOnlyList<FolderNode> tree = await NewHandler().HandleAsync();

        FolderNode kidzania = tree.Single().Children.Single();
        Assert.Equal(2, kidzania.ItemCount);
        FolderNode signs = Assert.Single(kidzania.Children);
        Assert.Equal("signs", signs.Name);
        Assert.Equal(1, signs.ItemCount);
        Assert.Equal(@"20250419 - Kidzania\signs", signs.RelativeFolder);
    }

    [Fact]
    public async Task Folders_CountPhotosOnlySoTheyMatchWhatSelectingThemShows()
    {
        // The grid shows photos, so a folder promising 3 and delivering 2 would
        // be the tree lying about itself.
        Add(@"trip\a.jpg");
        Add(@"trip\b.jpg");
        Add(@"trip\clip.mov", AssetKind.Video);

        IReadOnlyList<FolderNode> tree = await NewHandler().HandleAsync();

        Assert.Equal(2, tree.Single().Children.Single().ItemCount);
    }

    [Fact]
    public async Task Folders_OmitAFolderHoldingOnlyVideos()
    {
        Add(@"photos\a.jpg");
        Add(@"clips\only.mov", AssetKind.Video);

        IReadOnlyList<FolderNode> tree = await NewHandler().HandleAsync();

        Assert.Equal(["photos"], tree.Single().Children.Select(c => c.Name));
    }

    [Fact]
    public async Task Folders_AreEmptyBeforeAnythingIsIndexed() =>
        Assert.Empty(await NewHandler().HandleAsync());

    private void Add(string relativePath, AssetKind kind = AssetKind.Photo)
    {
        _db.Assets.Add(new Asset
        {
            Id = _nextId++,
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1,
            ModifiedUtc = new DateTime(2020, 1, 1),
            CreatedUtc = new DateTime(2020, 1, 1),
            IndexedUtc = new DateTime(2020, 1, 1),
            Kind = kind,
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
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
