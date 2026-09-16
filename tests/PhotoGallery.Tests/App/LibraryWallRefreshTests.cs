using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The Library wall, after something else has changed the library.
/// </summary>
/// <remarks>
/// Library is the one screen loaded once and kept, so that switching sections is
/// instant - and nothing ever told it the library had moved underneath it.
/// Narrowing to "Not in an album", filing three photographs into a new album and
/// then removing that album left the wall showing the count and the pictures read
/// before any of it. Only a filter chip, which re-runs the query, put it right.
///
/// <para>The same hole People and Albums already had, fixed in the same place and
/// the same way: the shell listens on behalf of whichever screen is stale.
/// <see cref="AlbumWallRefreshTests"/> is its mirror image.</para>
/// </remarks>
public sealed class LibraryWallRefreshTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    private readonly int _album;
    private readonly Asset _loose;
    private readonly Asset[] _filed;

    /// <summary>How many times the wall has read the library.</summary>
    private int _reads;

    public LibraryWallRefreshTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-library-wall-{Guid.NewGuid():N}");
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
        _loose = Photo("loose.jpg");
        _filed = [Photo("bali-1.jpg"), Photo("bali-2.jpg"), Photo("bali-3.jpg")];

        // The real reader and the real repositories, because the claim is about
        // one screen seeing what another screen wrote.
        _services = new ServiceCollection()
            .AddSingleton<IGalleryReader>(new SqliteGalleryReader(_db))
            .AddSingleton<IAlbumRepository>(new SqliteAlbumRepository(_db))
            .AddSingleton<ICollectionRepository>(new SqliteCollectionRepository(_db))
            .AddSingleton<ILibraryIndex>(new SqliteLibraryIndex(_db))
            .AddSingleton<IThumbnailStore>(thumbnails)
            .AddTransient<QueryGalleryHandler>()
            .AddTransient<GetFolderTreeHandler>()
            .BuildServiceProvider();

        var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

        var albums = new SqliteAlbumRepository(_db);
        _album = albums.CreateAsync("Bali").GetAwaiter().GetResult();
        albums.AddAsync(_album, [.. _filed.Select(photo => photo.Id)])
            .GetAwaiter().GetResult();

        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, thumbnails),
            new PeopleViewModel(scopeFactory, thumbnails),
            new DuplicatesViewModel(scopeFactory, thumbnails),
            thumbnails,
            new SilentLog());

        // Every read of the wall raises this, and nothing else in these tests
        // puts the grid to work - so counting it counts reads.
        _main.Gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GalleryViewModel.IsLoading) && _main.Gallery.IsLoading)
            {
                Interlocked.Increment(ref _reads);
            }
        };
    }

    /// <summary>
    /// The bug as it was reported: an album removed on Albums, and the wall on
    /// the next visit to Library.
    /// </summary>
    [Fact]
    public async Task AnAlbumRemovedElsewhere_PutsItsPhotographsBackOnTheWall()
    {
        await NarrowedToOutsideAlbumsAsync();

        await OpenAsync(ActivitySection.AlbumsKey);
        await WaitFor(() => _main.Albums.Wall.Count == 1, "the wall of albums to be read");
        _main.Albums.OpenCommand.Execute(_main.Albums.Wall[0]);
        await _main.Albums.DeleteCommand.ExecuteAsync(null);

        await OpenAsync(ActivitySection.LibraryKey);

        await WaitFor(
            () => Shown().Count == 4,
            "the wall to show the photographs the removed album let loose");
        Assert.Equal([_loose.Id, .. _filed.Select(photo => photo.Id)], Shown().Order());

        // The header too, which is the half of this the bug was reported from.
        // Only the count: nothing here has a rendition on disk, so the line goes
        // on to say how many are still to be prepared.
        Assert.StartsWith(
            "4 pictures outside an album", _main.Gallery.CountSummary, StringComparison.Ordinal);
    }

    /// <summary>
    /// And where Library is the screen being looked at, without leaving it.
    /// </summary>
    /// <remarks>
    /// The shape of answering album suggestions in the photo viewer: the viewer is
    /// drawn over Library, each answer is written as it is given, and Albums
    /// settles and says so once the viewer is out of the way. Nothing changes
    /// section, so the next visit to Library is this one.
    /// </remarks>
    [Fact]
    public async Task AChangeElsewhereWhileLibraryIsOpen_RedrawsTheWallWhereItStands()
    {
        await NarrowedToOutsideAlbumsAsync();

        await new SqliteAlbumRepository(_db).DeleteAsync(_album);
        await _main.Albums.SettleAfterDecidingAsync();

        await WaitFor(() => Shown().Count == 4, "the wall to be redrawn where it stands");
        Assert.True(_main.ShowLibrary, "the reader never left Library");
    }

    /// <summary>
    /// The wall's own changes are left alone, because they reload the wall
    /// themselves and a second read underneath the first would build it twice.
    /// </summary>
    [Fact]
    public async Task TheWallsOwnChange_IsNotReadASecondTime()
    {
        await NarrowedToOutsideAlbumsAsync();
        int reads = Volatile.Read(ref _reads);

        // Deleting ticked photographs: the grid announces it and catches itself
        // up, and that one read is all it may cost.
        await _main.Gallery.AfterChosenDeletedAsync(
            new PhotoRemovalResult(1, [], false, [], []));

        await StaysAt(reads + 1, "deleting photographs on the wall");

        // And the count is not simply stuck: the same event from another screen
        // still reads.
        await _main.Albums.SettleAfterDecidingAsync();
        await WaitFor(
            () => Volatile.Read(ref _reads) > reads + 1,
            "a change reported by another screen to read the wall again");
    }

    /// <summary>Library open, narrowed to the photographs in no album.</summary>
    private async Task NarrowedToOutsideAlbumsAsync()
    {
        await OpenAsync(ActivitySection.LibraryKey);
        await WaitFor(() => Shown().Count == 4, "the wall to be read");

        await _main.Gallery.ShowOutsideAlbumsCommand.ExecuteAsync(null);

        Assert.Equal([_loose.Id], Shown());
    }

    /// <summary>
    /// Opens a section, as clicking its row in the activity bar does.
    /// </summary>
    /// <remarks>
    /// The change starts the screen's read and does not hand it back, so every
    /// caller waits for what it is expecting rather than for this.
    /// </remarks>
    private Task OpenAsync(string key)
    {
        _main.SelectedSection = _main.TopSections.Single(section => section.Key == key);
        return Task.CompletedTask;
    }

    /// <summary>What the wall is drawing.</summary>
    private List<int> Shown() =>
        [.. _main.Gallery.Rows.SelectMany(row => row.Tiles).Select(tile => tile.Item.Id)];

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
    /// The reload runs from an event handler, as it does in the app: the shell is
    /// told the library changed and re-reads the stale screen without anybody
    /// awaiting it.
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

    /// <summary>
    /// The other half of <see cref="WaitFor"/>, for a read that must not happen.
    /// </summary>
    /// <remarks>
    /// Watching for a while is all a fire-and-forget handler allows. Too short a
    /// watch can only weaken this test, never redden it on a slow machine.
    /// </remarks>
    private async Task StaysAt(int reads, string what)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            Assert.True(
                Volatile.Read(ref _reads) <= reads,
                $"the wall was read again after {what}.");

            await Task.Delay(10);
        }
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
