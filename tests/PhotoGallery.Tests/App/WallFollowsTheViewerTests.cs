using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The wall keeping up with what is done in the photograph viewer.
/// </summary>
/// <remarks>
/// With "Not in an album" on, filing a photograph from the viewer wrote the move
/// and updated the line beside the picture - and left the wall exactly as it
/// was. The photograph stayed under a filter it no longer answered, and the
/// count stayed where it had been, until something happened to read the whole
/// wall again. "No faces named" had the same hole for naming somebody.
///
/// <para>Against the real service graph and a real file, because the answer to
/// "does this photograph still belong?" is the reader's, and a fake reader would
/// test only that the question was asked.</para>
/// </remarks>
public sealed class WallFollowsTheViewerTests : IDisposable
{
    private const int Photographs = 6;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly GalleryViewModel _gallery;

    public WallFollowsTheViewerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-wall-follows-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _services = new ServiceCollection()
            .AddPhotoGalleryInfrastructure(_root)
            .AddPhotoGalleryHandlers()
            .BuildServiceProvider();

        using (IServiceScope scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<GalleryDbContext>().Database.Migrate();
        }

        Seed();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IThumbnailStore>());

        _gallery.SetColumns(3);
    }

    /// <summary>
    /// Filed from the viewer under "Not in an album", a photograph leaves the
    /// wall once the viewer moves on.
    /// </summary>
    [Fact]
    public async Task FiledUnderNotInAnAlbum_ItLeavesTheWallWhenTheViewerMovesOn()
    {
        await _gallery.ShowOutsideAlbumsCommand.ExecuteAsync(null);
        Assert.Equal(Photographs, Wall().Count);

        GalleryTile filed = Tile("2.jpg");
        GalleryTile after = Wall()[Wall().IndexOf(filed) + 1];
        List<GalleryTile> others = [.. Wall().Where(tile => !ReferenceEquals(tile, filed))];

        _gallery.OpenPhotoCommand.Execute(filed);
        await FileOpenPhotoAsync("Bali");

        _gallery.NextPhotoCommand.Execute(null);
        await _gallery.WallSettled;

        Assert.DoesNotContain(filed, Wall());
        Assert.Equal(Photographs - 1, Wall().Count);
        Assert.StartsWith($"{Photographs - 1}", _gallery.CountSummary, StringComparison.Ordinal);

        // The viewer went where Next always goes.
        Assert.Same(after, _gallery.OpenTile);

        // And nothing else was read again: every other tile is the very object
        // it was, picture and all, rather than a wall rebuilt to lose one.
        Assert.All(others, tile => Assert.Contains(tile, Wall()));
    }

    /// <summary>
    /// But not while the viewer is still on it.
    /// </summary>
    /// <remarks>
    /// The line saying where it went is read beside the photograph it is about,
    /// and the viewer's arrows still have to know where they are - a tile that
    /// left the wall from under an open viewer would have no "next".
    /// </remarks>
    [Fact]
    public async Task WhileTheViewerIsStillOnIt_ThePhotographStaysOnTheWall()
    {
        await _gallery.ShowOutsideAlbumsCommand.ExecuteAsync(null);

        GalleryTile filed = Tile("2.jpg");
        _gallery.OpenPhotoCommand.Execute(filed);
        await FileOpenPhotoAsync("Bali");

        Assert.Contains(filed, Wall());
        Assert.True(_gallery.NextPhotoCommand.CanExecute(null));
        Assert.Contains("Bali", _gallery.AlbumNotice, StringComparison.Ordinal);
    }

    /// <summary>Closing the viewer is leaving it too.</summary>
    [Fact]
    public async Task ClosingTheViewer_AlsoTakesTheFiledPhotographOff()
    {
        await _gallery.ShowOutsideAlbumsCommand.ExecuteAsync(null);

        GalleryTile filed = Tile("4.jpg");
        _gallery.OpenPhotoCommand.Execute(filed);
        await FileOpenPhotoAsync("Bali");

        _gallery.ClosePhotoCommand.Execute(null);
        await _gallery.WallSettled;

        Assert.DoesNotContain(filed, Wall());
    }

    /// <summary>
    /// With no filter that cares, filing changes nothing about the wall.
    /// </summary>
    [Fact]
    public async Task WithNothingNarrowingTheWall_AFiledPhotographStays()
    {
        await _gallery.LoadAsync();

        GalleryTile filed = Tile("2.jpg");
        _gallery.OpenPhotoCommand.Execute(filed);
        await FileOpenPhotoAsync("Bali");

        _gallery.NextPhotoCommand.Execute(null);
        await _gallery.WallSettled;

        Assert.Contains(filed, Wall());
        Assert.Equal(Photographs, Wall().Count);
    }

    /// <summary>
    /// Naming somebody under "No faces named" takes the photograph off - but only
    /// once the viewer moves on, so the rest of a group shot can still be named.
    /// </summary>
    /// <remarks>
    /// Settling at once was the obvious design and the wrong one: a photograph
    /// that vanished after the first of four names would take the other three
    /// questions with it.
    /// </remarks>
    [Fact]
    public async Task NamedUnderNoFacesNamed_ItLeavesOnlyOnceTheViewerMovesOn()
    {
        await _gallery.ShowWithoutNamedFacesCommand.ExecuteAsync(null);
        Assert.Equal(Photographs, Wall().Count);

        GalleryTile group = Tile("3.jpg");
        _gallery.ShowFaceNames = true;
        _gallery.OpenPhotoCommand.Execute(group);
        await TestWait.ForAsync(
            () => _gallery.OpenFaces.Count == 2,
            "both faces to be drawn",
            attempts: 300);

        _gallery.BeginNamingFaceCommand.Execute(_gallery.OpenFaces[0]);
        _gallery.Picker.Typed = "Shirley";
        await _gallery.Picker.AddCommand.ExecuteAsync(null);

        // Named once, and still here with its other face waiting.
        Assert.Contains(group, Wall());
        Assert.Same(group, _gallery.OpenTile);

        _gallery.ClosePhotoCommand.Execute(null);
        await _gallery.WallSettled;

        Assert.DoesNotContain(group, Wall());
        Assert.Equal(Photographs - 1, Wall().Count);
    }

    /// <summary>
    /// Taken out of an album from another screen's grid, a photograph comes back
    /// onto a wall showing what is in no album.
    /// </summary>
    /// <remarks>
    /// The other direction, and the one case the wall cannot settle by itself:
    /// where the photograph goes in the order is the query's to say, so the wall
    /// is read again - behind the screen it was opened from.
    /// </remarks>
    [Fact]
    public async Task TakenOutOfAnAlbumElsewhere_ItComesBackOntoTheWall()
    {
        int filedId = await FileDirectlyAsync("5.jpg", "Sentosa");

        await _gallery.ShowOutsideAlbumsCommand.ExecuteAsync(null);
        Assert.DoesNotContain(Wall(), tile => tile.Item.Id == filedId);

        // Opened the way the People screen and an album open one: from their own
        // grid, holding their own tile for it.
        var elsewhere = new TileWindow(_services.GetRequiredService<IThumbnailStore>());
        elsewhere.Fill([new GalleryTile(ItemFor(filedId))]);
        _gallery.OpenFrom(new PlainGrid(elsewhere), elsewhere[0]);
        await TestWait.ForAsync(
            () => _gallery.OpenPhotoAlbum is not null,
            "its album to be read",
            attempts: 300);

        await _gallery.AddToAlbumCommand.ExecuteAsync(null);
        await _gallery.Albums.TakeOutCommand.ExecuteAsync(null);

        _gallery.ClosePhotoCommand.Execute(null);
        await _gallery.WallSettled;

        Assert.Contains(Wall(), tile => tile.Item.Id == filedId);
        Assert.Equal(Photographs, Wall().Count);
    }

    /// <summary>
    /// A write that lands after the viewer has already moved on is checked at
    /// once.
    /// </summary>
    /// <remarks>
    /// Naming a face hands the screen back before its write - which re-proposes
    /// that person across the whole library and takes seconds - so "name it,
    /// next" leaves the photograph before the write lands. It used to be marked
    /// for a leave that had already happened, and so was never checked at all:
    /// it stayed on "No faces named" with a name on it. Landed here without
    /// racing a real write, by marking after the leave.
    /// </remarks>
    [Fact]
    public async Task AWriteLandingAfterTheViewerMovedOn_IsSettledAtOnce()
    {
        await _gallery.ShowOutsideAlbumsCommand.ExecuteAsync(null);

        GalleryTile filed = Tile("2.jpg");
        _gallery.OpenPhotoCommand.Execute(filed);

        // The person moves on first; the write lands afterwards.
        _gallery.NextPhotoCommand.Execute(null);
        await FileDirectlyAsync("2.jpg", "Bali");
        _gallery.ChangedWhileOpen(filed.Item.Id, from: null);
        await _gallery.WallSettled;

        Assert.DoesNotContain(filed, Wall());
    }

    /// <summary>
    /// Naming a face on one copy of a photograph takes every copy off "No faces
    /// named".
    /// </summary>
    /// <remarks>
    /// A name given on one copy is given on all of them - AssignFacesHandler
    /// widens it to every row with the same picture and the same box - so the
    /// copies stopped answering the wall's question together, and only the open
    /// one was taken off.
    /// </remarks>
    [Fact]
    public async Task NamingOneCopy_TakesEveryCopyOffNoFacesNamed()
    {
        (int first, int second) = AddTwoCopies();

        await _gallery.ShowWithoutNamedFacesCommand.ExecuteAsync(null);
        GalleryTile open = TileById(second);

        await NameTheOnlyFaceAsync(open, "Jia Le");

        _gallery.ClosePhotoCommand.Execute(null);
        await _gallery.WallSettled;

        Assert.DoesNotContain(Wall(), tile => tile.Item.Id == first);
        Assert.DoesNotContain(Wall(), tile => tile.Item.Id == second);
    }

    /// <summary>
    /// But never the copy the viewer is showing.
    /// </summary>
    /// <remarks>
    /// Two copies of one picture sit side by side, so "name it, next" lands on
    /// the other copy. Taking that one off the wall from under the viewer would
    /// strand it - it steps by the tile's place on the wall, and a tile that is
    /// not there has no next. It waits for the viewer to move off it.
    /// </remarks>
    [Fact]
    public async Task TheCopyOnScreen_WaitsForTheViewerToMoveOff()
    {
        (int first, int second) = AddTwoCopies();

        await _gallery.ShowWithoutNamedFacesCommand.ExecuteAsync(null);
        GalleryTile named = TileById(second);
        GalleryTile copy = TileById(first);

        await NameTheOnlyFaceAsync(named, "Jia Le");

        _gallery.NextPhotoCommand.Execute(null);
        Assert.Same(copy, _gallery.OpenTile);
        await _gallery.WallSettled;

        // The one that was left is gone; the one on screen is still on the wall,
        // so the viewer still knows where it is.
        Assert.DoesNotContain(named, Wall());
        Assert.Contains(copy, Wall());
        Assert.True(_gallery.NextPhotoCommand.CanExecute(null));

        _gallery.ClosePhotoCommand.Execute(null);
        await _gallery.WallSettled;

        Assert.DoesNotContain(copy, Wall());
    }

    /// <summary>
    /// The busy state ends when the wall has been read, not when its pictures
    /// have been decoded.
    /// </summary>
    /// <remarks>
    /// Decoding the first screen waits a second for the view to stop moving, by
    /// design. Held under the busy state, the veil kept a wall that was already
    /// right faded and out of reach for a second and a half after every reload.
    /// </remarks>
    [Fact]
    public async Task TheBusyStateEndsWhenTheWallIsRead_NotWhenItsPicturesAre()
    {
        Task load = _gallery.LoadAsync();

        await TestWait.ForAsync(
            () => !_gallery.IsLoading && Wall().Count == Photographs,
            "the wall to be read",
            attempts: 300);

        Assert.False(load.IsCompleted, "the busy state lasted until the pictures were decoded");

        await load;
    }

    /// <summary>
    /// While the wall is read again, it says so and is out of reach.
    /// </summary>
    /// <remarks>
    /// A full read is not instant on a large library - the thumbnails on disk are
    /// checked one by one, 370 ms for 9,336 of them on the first real one - and a
    /// wall that sat there unchanged for that long read as the app having ignored
    /// what was just done. Markup, so read as text.
    /// </remarks>
    [Fact]
    public void WhileTheWallIsReadAgain_ItSaysSoAndIsOutOfReach()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        int count = window.IndexOf("Text=\"{Binding Gallery.CountSummary}\"", StringComparison.Ordinal);
        int updating = window.IndexOf("Text=\"Updating...\"", StringComparison.Ordinal);
        Assert.True(count > 0 && updating > count, "nothing beside the count says the wall is being read");

        int wall = window.IndexOf("x:Name=\"GalleryGrid\"", StringComparison.Ordinal);
        int empty = window.IndexOf("Text=\"{Binding Gallery.EmptyMessage}\"", StringComparison.Ordinal);
        string over = window[wall..empty];

        // A veil over the list, gated on the same flag, and able to take a click.
        Assert.Contains("Visibility=\"{Binding Gallery.IsLoading,", over, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource Editor.Background}\"", over, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- helpers

    private List<GalleryTile> Wall() => [.. _gallery.Rows.SelectMany(row => row.Tiles)];

    private GalleryTile Tile(string fileName) =>
        Wall().Single(tile => tile.FileName == fileName);

    private GalleryTile TileById(int assetId) =>
        Wall().Single(tile => tile.Item.Id == assetId);

    /// <summary>
    /// Two copies of one photograph - the same picture, one face in the same
    /// place on each - newer than everything else, so they lead the wall side by
    /// side.
    /// </summary>
    private (int First, int Second) AddTwoCopies()
    {
        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();

        int[] ids = [Photographs + 1, Photographs + 2];
        foreach (int id in ids)
        {
            db.Assets.Add(new Asset
            {
                Id = id,
                PhotoSourceId = 1,
                RelativePath = $@"copies\{id}.jpg",
                Length = 1,
                ModifiedUtc = new DateTime(2021, 1, 1),
                CreatedUtc = new DateTime(2021, 1, 1),
                IndexedUtc = new DateTime(2021, 1, 1),
                Kind = AssetKind.Photo,
                ThumbnailName = "the-same-picture",
            });
        }

        db.SaveChanges();

        foreach (int id in ids)
        {
            db.Faces.Add(new Face
            {
                AssetId = id,
                Bounds = new FaceBounds(20, 20, 50, 50),
                DetectScore = 0.9f,
                Embedding = TestEmbeddings.At(30),
            });
        }

        db.SaveChanges();

        return (ids[0], ids[1]);
    }

    /// <summary>Opens a photograph with one face and names it, waiting for the write.</summary>
    private async Task NameTheOnlyFaceAsync(GalleryTile tile, string name)
    {
        _gallery.ShowFaceNames = true;
        _gallery.OpenPhotoCommand.Execute(tile);
        await TestWait.ForAsync(
            () => _gallery.OpenFaces.Count == 1,
            "its face to be drawn",
            attempts: 300);

        _gallery.BeginNamingFaceCommand.Execute(_gallery.OpenFaces[0]);
        _gallery.Picker.Typed = name;
        await _gallery.Picker.AddCommand.ExecuteAsync(null);
    }

    /// <summary>Files the open photograph through the viewer's own list.</summary>
    private async Task FileOpenPhotoAsync(string album)
    {
        await _gallery.AddToAlbumCommand.ExecuteAsync(null);
        _gallery.Albums.Typed = album;
        await _gallery.Albums.CreateCommand.ExecuteAsync(null);
    }

    /// <summary>Files one photograph by writing it, as another screen would.</summary>
    private async Task<int> FileDirectlyAsync(string fileName, string album)
    {
        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        int assetId = db.Assets.Single(asset => asset.RelativePath.EndsWith(fileName)).Id;

        IAlbumRepository albums = scope.ServiceProvider.GetRequiredService<IAlbumRepository>();
        int id = await albums.CreateAsync(album);
        await albums.AddAsync(id, [assetId]);

        return assetId;
    }

    private GalleryItem ItemFor(int assetId)
    {
        using IServiceScope scope = _services.CreateScope();
        Asset asset = scope.ServiceProvider.GetRequiredService<GalleryDbContext>()
            .Assets.AsNoTracking().Single(row => row.Id == assetId);

        string name = Path.GetFileName(asset.RelativePath);

        return new GalleryItem(
            asset.Id, asset.RelativePath, name, "2020",
            Path.Combine(_root, asset.RelativePath), asset.ThumbnailName ?? name,
            asset.ModifiedUtc, asset.ModifiedUtc, 0, AssetKind.Photo);
    }

    private void Seed()
    {
        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();

        db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });

        for (int i = 0; i < Photographs; i++)
        {
            db.Assets.Add(new Asset
            {
                Id = i + 1,
                PhotoSourceId = 1,
                RelativePath = $@"2020\{i}.jpg",
                Length = 1,
                ModifiedUtc = new DateTime(2020, 1, 1).AddMinutes(i),
                CreatedUtc = new DateTime(2020, 1, 1),
                IndexedUtc = new DateTime(2020, 1, 1),
                Kind = AssetKind.Photo,
            });
        }

        db.SaveChanges();

        // A group shot: two faces, nobody named, on 3.jpg.
        foreach (double degrees in new[] { 0d, 90d })
        {
            db.Faces.Add(new Face
            {
                AssetId = 4,
                Bounds = new FaceBounds(10 + (int)degrees, 10, 40, 40),
                DetectScore = 0.9f,
                Embedding = TestEmbeddings.At(degrees),
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
            // A temporary folder; SQLite's pooled handle may outlive the provider.
        }
    }
}
