using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The wall of albums, after something else has changed one of them.
/// </summary>
/// <remarks>
/// Which picture an album shows is chosen in the photo viewer, and the viewer is
/// drawn over the content area rather than inside it - so choosing a cover
/// changes the screen underneath without ever changing section. Nothing told the
/// wall, so the card kept the picture it had decoded before the choice, and
/// pressing Back merely uncovered it. Leaving the section and coming back put it
/// right, which is the shape of a screen nobody told.
///
/// <para>The same hole People already had, fixed the same way: the shell listens
/// to the viewer on behalf of whichever screen is open behind it.</para>
/// </remarks>
public sealed class AlbumWallRefreshTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    private readonly int _album;
    private readonly Asset _crowd;
    private readonly Asset _plain;

    public AlbumWallRefreshTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-album-wall-{Guid.NewGuid():N}");
        string library = Path.Combine(_root, "library");
        Directory.CreateDirectory(library);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(library, "index.db")}")
                .Options);
        _db.Database.Migrate();

        var workingFolder = new WorkingFolder(library);
        workingFolder.EnsureCreated();
        var thumbnails = new FileSystemThumbnailStore(workingFolder);

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _crowd = Photo("crowd.jpg");
        _plain = Photo("plain.jpg");
        _db.Faces.Add(new Face
        {
            AssetId = _crowd.Id,
            Bounds = new FaceBounds(10, 10, 40, 40),
            DetectScore = 0.99f,
            Embedding = TestEmbeddings.At(0),
        });
        _db.SaveChanges();

        // The real repositories, because the claim is about one screen seeing
        // what another screen wrote.
        _services = new ServiceCollection()
            .AddSingleton<IAlbumRepository>(new SqliteAlbumRepository(_db))
            .AddSingleton<ICollectionRepository>(new SqliteCollectionRepository(_db))
            .AddSingleton<ILibraryIndex>(new SqliteLibraryIndex(_db))
            .AddSingleton<IThumbnailStore>(thumbnails)
            .BuildServiceProvider();

        var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

        var albums = new SqliteAlbumRepository(_db);
        _album = albums.CreateAsync("Bali").GetAwaiter().GetResult();
        albums.AddAsync(_album, [_crowd.Id, _plain.Id]).GetAwaiter().GetResult();

        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, thumbnails),
            new PeopleViewModel(scopeFactory, thumbnails),
            new DuplicatesViewModel(scopeFactory, thumbnails),
            thumbnails,
            new SilentLog());
    }

    /// <summary>
    /// Choosing a cover in the viewer changes the card behind it, without
    /// waiting for the reader to leave the section and come back.
    /// </summary>
    [Fact]
    public async Task ChoosingACoverInTheViewerRedrawsTheWallBehindIt()
    {
        await OpenTheAlbumsSectionAsync();

        // The rule's answer, which is the photograph with a face in it.
        Assert.Equal("crowd.jpg", Shown());

        Open(_plain);
        _main.Gallery.OpenPhotoAlbum =
            await new SqliteAlbumRepository(_db).FindForAssetAsync(_plain.Id);

        await _main.Gallery.MakeAlbumCoverCommand.ExecuteAsync(null);

        await WaitFor(() => Shown() == "plain.jpg", "the wall to show the chosen cover");
    }

    /// <summary>
    /// And it is the row on the wall that changes, not only the database - so
    /// pressing Back has nothing left to put right.
    /// </summary>
    /// <remarks>
    /// Back is <c>CloseAlbumCommand</c>, which sets the open album to null and
    /// reads nothing. That is right, and is why the refresh has to have happened
    /// already.
    /// </remarks>
    [Fact]
    public async Task AfterPressingBackTheCardCarriesTheChosenCover()
    {
        await OpenTheAlbumsSectionAsync();

        _main.Albums.OpenCommand.Execute(_main.Albums.Wall[0]);
        Assert.NotNull(_main.Albums.Selected);

        Open(_plain);
        _main.Gallery.OpenPhotoAlbum =
            await new SqliteAlbumRepository(_db).FindForAssetAsync(_plain.Id);

        await _main.Gallery.MakeAlbumCoverCommand.ExecuteAsync(null);
        await WaitFor(() => Shown() == "plain.jpg", "the wall to show the chosen cover");

        _main.Albums.CloseAlbumCommand.Execute(null);

        Assert.Null(_main.Albums.Selected);
        Assert.Equal("plain.jpg", Shown());
    }

    private async Task OpenTheAlbumsSectionAsync()
    {
        _main.SelectedSection =
            _main.TopSections.Single(section => section.Key == ActivitySection.AlbumsKey);

        await WaitFor(() => _main.Albums.Wall.Count == 1, "the wall to be read");
    }

    /// <summary>The rendition the card would draw.</summary>
    private string? Shown() =>
        _main.Albums.Wall.Count == 1 ? _main.Albums.Wall[0].Summary.CoverThumbnailName : null;

    /// <summary>A picture open in the viewer, as clicking one in the grid leaves it.</summary>
    private void Open(Asset asset) =>
        _main.Gallery.OpenPhotoCommand.Execute(new GalleryTile(new GalleryItem(
            asset.Id,
            asset.RelativePath,
            asset.RelativePath,
            string.Empty,
            Path.Combine(_root, asset.RelativePath),
            asset.ThumbnailName,
            null,
            asset.TakenUtc ?? asset.ModifiedUtc,
            0,
            AssetKind.Photo)));

    private Asset Photo(string name)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = name,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = name,
            Length = 1024 + name.Length,
            ModifiedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TakenUtc = new DateTime(2019, 7, 4, 10, 0, 0, DateTimeKind.Unspecified),
            Width = 1000,
            Height = 800,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        return asset;
    }

    /// <summary>
    /// Waits on work a screen starts and does not hand back.
    /// </summary>
    /// <remarks>
    /// The reload runs from an event handler, as it does in the app: the shell
    /// is told the library changed and re-reads the screen behind the viewer
    /// without anybody awaiting it.
    /// </remarks>
    private static async Task WaitFor(Func<bool> done, string what)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (done())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    private sealed class SilentLog : IActivityLog
    {
        public void Append(string line)
        {
        }
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test.
        }
    }
}
