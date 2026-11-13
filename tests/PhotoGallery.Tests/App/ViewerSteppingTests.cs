using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What next and previous step through, now that two screens share one viewer.
/// </summary>
/// <remarks>
/// The People screen shows one person's pictures in its own grid and opens them
/// in the library's viewer. Asking the library where one of those tiles sat
/// answers "nowhere", which broke this twice in a row: first by stepping to the
/// library's opening picture, then - after that was guarded - by doing nothing
/// at all. The viewer has to be told which grid a picture came from.
/// </remarks>
public sealed class ViewerSteppingTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly GalleryViewModel _gallery;
    private readonly TileWindow _elsewhere;

    public ViewerSteppingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        string database = Path.Combine(_root, "index.db");
        Seed(database);

        _services = new ServiceCollection()
            .AddDbContext<GalleryDbContext>(
                options => options.UseSqlite($"Data Source={database}"))
            .AddScoped<IGalleryReader, SqliteGalleryReader>()
            .AddTransient<QueryGalleryHandler>()
            .BuildServiceProvider();

        var thumbnails = new FileSystemThumbnailStore(workingFolder);
        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(), thumbnails);

        // Laid out as a window would lay it out; the default of one across would
        // put every picture on its own row.
        _gallery.SetColumns(4);

        // Another screen's grid, holding pictures the library's grid never has.
        _elsewhere = new TileWindow(thumbnails);
        _elsewhere.SetColumns(4);
        _elsewhere.Fill(Tiles(900, 3));
    }

    [Fact]
    public async Task Next_StepsThroughTheGridThePictureCameFrom()
    {
        await _gallery.LoadAsync();
        _gallery.OpenFrom(new PlainGrid(_elsewhere), _elsewhere[0]);

        Assert.True(_gallery.NextPhotoCommand.CanExecute(null));
        _gallery.NextPhotoCommand.Execute(null);

        Assert.Same(_elsewhere[1], _gallery.OpenTile);
    }

    [Fact]
    public async Task Previous_StepsBackThroughThatSameGrid()
    {
        await _gallery.LoadAsync();
        _gallery.OpenFrom(new PlainGrid(_elsewhere), _elsewhere[2]);

        Assert.True(_gallery.PreviousPhotoCommand.CanExecute(null));
        _gallery.PreviousPhotoCommand.Execute(null);

        Assert.Same(_elsewhere[1], _gallery.OpenTile);
    }

    [Fact]
    public async Task Next_StopsAtTheEndOfTheOtherGridRatherThanItsOwn()
    {
        // The library holds more pictures than the person does. Reading the
        // limit off the wrong one walks past the end of what is on screen.
        await _gallery.LoadAsync();
        _gallery.OpenFrom(new PlainGrid(_elsewhere), _elsewhere[2]);

        Assert.False(_gallery.NextPhotoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Previous_IsRefusedOnTheFirstPictureOfTheOtherGrid()
    {
        await _gallery.LoadAsync();
        _gallery.OpenFrom(new PlainGrid(_elsewhere), _elsewhere[0]);

        Assert.False(_gallery.PreviousPhotoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Closing_HandsTheViewerBackToTheLibrary()
    {
        // Or the next picture opened from the grid would still step through
        // whichever screen was looked at last.
        await _gallery.LoadAsync();
        _gallery.OpenFrom(new PlainGrid(_elsewhere), _elsewhere[0]);
        _gallery.ClosePhoto();

        _gallery.OpenPhotoCommand.Execute(_gallery.Rows[0].Tiles[0]);
        _gallery.NextPhotoCommand.Execute(null);

        Assert.Same(_gallery.Rows[0].Tiles[1], _gallery.OpenTile);
    }

    [Fact]
    public async Task Opening_FromTheLibraryStillStepsThroughTheLibrary()
    {
        await _gallery.LoadAsync();

        _gallery.OpenPhotoCommand.Execute(_gallery.Rows[0].Tiles[0]);
        _gallery.NextPhotoCommand.Execute(null);

        Assert.Same(_gallery.Rows[0].Tiles[1], _gallery.OpenTile);
    }

    /// <summary>
    /// Deleting a photograph opened from another screen takes it off the
    /// library's wall too.
    /// </summary>
    /// <remarks>
    /// Which grid the picture came from decides where the viewer goes next; it
    /// says nothing about the library's own wall, which is holding a tile for a
    /// photograph that has just been deleted whichever screen deleted it. This
    /// branch closed the viewer and returned, and the wall was left as it was.
    ///
    /// <para>What that looks like is a photograph you can still open and that
    /// has nothing behind it: the tile draws from the cached thumbnail, which
    /// outlives the delete, while the album, the faces and every fact about the
    /// file are read by an id the index no longer has, so the panel beside it
    /// comes up empty. It was reported as "this photo looks strange, no image
    /// path" - and it was not strange, it was gone.</para>
    /// </remarks>
    [Fact]
    public async Task DeletingAPictureOpenedElsewhere_TakesItOffTheLibraryWallToo()
    {
        await _gallery.LoadAsync();

        GalleryTile going = _gallery.Rows.SelectMany(row => row.Tiles).First();
        int id = going.Item.Id;

        // Opened the way the People screen and an album open one: through their
        // own grid, so the viewer steps through theirs rather than the library's.
        _elsewhere.Fill([going]);
        _gallery.OpenFrom(new PlainGrid(_elsewhere), going);

        Forget(id);

        await _gallery.AfterOpenPhotoDeletedAsync(new PhotoRemovalResult(1, [], false, [], []));

        // The viewer stands down, because the grid it was stepping through
        // belongs to a screen that rebuilds itself.
        Assert.False(_gallery.IsViewerOpen);

        // And the wall has forgotten it, so there is no tile left to open onto
        // a photograph that is not there.
        Assert.DoesNotContain(
            _gallery.Rows.SelectMany(row => row.Tiles),
            tile => tile.Item.Id == id);
    }

    /// <summary>Takes one photograph out of the index, as a deletion does.</summary>
    private void Forget(int assetId)
    {
        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();

        db.Assets.Remove(db.Assets.Single(asset => asset.Id == assetId));
        db.SaveChanges();
    }

    private static IEnumerable<GalleryTile> Tiles(int firstId, int count) =>
        Enumerable.Range(0, count).Select(i => new GalleryTile(new GalleryItem(
            firstId + i,
            $@"2015\{firstId + i}.jpg",
            $"{firstId + i}.jpg",
            "2015",
            $@"C:\pictures\2015\{firstId + i}.jpg",
            $"{firstId + i}",
            new DateTime(2014, 3, 11, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2014, 3, 11, 12, 0, 0, DateTimeKind.Utc),
            0,
            AssetKind.Photo)));

    private static void Seed(string database)
    {
        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={database}")
                .Options;

        using var db = new GalleryDbContext(options);
        db.Database.Migrate();
        db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = @"C:\pictures" });

        for (int i = 0; i < 40; i++)
        {
            db.Assets.Add(new Asset
            {
                Id = i + 1,
                PhotoSourceId = 1,
                RelativePath = $@"2020\{i:D4}.jpg",
                Length = 1,
                ModifiedUtc = new DateTime(2020, 1, 1).AddMinutes(i),
                CreatedUtc = new DateTime(2020, 1, 1),
                IndexedUtc = new DateTime(2020, 1, 1),
                Kind = AssetKind.Photo,
            });
        }

        db.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A handle the provider has not let go of yet.
        }
    }
}
