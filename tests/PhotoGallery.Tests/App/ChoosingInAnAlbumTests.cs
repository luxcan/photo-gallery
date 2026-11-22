using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.Shell;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Choosing several photographs inside an open album, as the library's wall
/// already could.
/// </summary>
/// <remarks>
/// The same choosing, deliberately - a click, a Shift run and the bar mean what
/// they mean on the library - so what is argued out here is only what an album
/// adds: a way out of the album that keeps the photographs, filing that names
/// the album they came from, and a choice that belongs to the album it was made
/// in. The real repositories, because every claim is about what was written.
/// </remarks>
public sealed class ChoosingInAnAlbumTests : IDisposable
{
    private readonly string _root;
    private readonly string _database;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    private readonly int _bali;
    private readonly int _java;
    private readonly Asset _beach;
    private readonly Asset _temple;

    public ChoosingInAnAlbumTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-album-choosing-{Guid.NewGuid():N}");
        string library = Path.Combine(_root, "library");
        Directory.CreateDirectory(library);
        _database = Path.Combine(library, "index.db");

        using (GalleryDbContext db = Open())
        {
            db.Database.Migrate();
            db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
            _beach = Photo(db, "beach.jpg");
            _temple = Photo(db, "temple.jpg");

            var albums = new SqliteAlbumRepository(db);
            _bali = albums.CreateAsync("Bali").GetAwaiter().GetResult();
            _java = albums.CreateAsync("Java").GetAwaiter().GetResult();
            albums.AddAsync(_bali, [_beach.Id, _temple.Id]).GetAwaiter().GetResult();
        }

        var workingFolder = new WorkingFolder(library);
        workingFolder.EnsureCreated();
        var thumbnails = new FileSystemThumbnailStore(workingFolder);

        // Scoped, as the app has them: the album's page and the albums are read
        // at the same time, and one context cannot serve two reads at once.
        _services = new ServiceCollection()
            .AddDbContext<GalleryDbContext>(
                options => options.UseSqlite($"Data Source={_database}"))
            .AddScoped<IAlbumRepository, SqliteAlbumRepository>()
            .AddScoped<ICollectionRepository, SqliteCollectionRepository>()
            .AddScoped<ILibraryIndex, SqliteLibraryIndex>()
            .AddScoped<IGalleryReader, SqliteGalleryReader>()
            .AddTransient<QueryGalleryHandler>()
            .AddSingleton<IThumbnailStore>(thumbnails)
            .BuildServiceProvider();

        var scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();

        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, thumbnails),
            new PeopleViewModel(scopeFactory, thumbnails),
            new DuplicatesViewModel(scopeFactory, thumbnails),
            thumbnails,
            new SilentLog());
    }

    /// <summary>The circle on an album's photograph starts the mode, and the bar is the album's.</summary>
    [Fact]
    public async Task TickingAnAlbumsPhotographBringsUpTheBarForThatAlbum()
    {
        await OpenBaliAsync();

        Assert.False(_main.ShowChoosingBar);

        _main.Albums.Choice.ChoosePhoto(Tile(0), extend: false);

        Assert.True(_main.Albums.Choice.IsChoosing);
        Assert.True(_main.ShowChoosingBar);
        Assert.Same(_main.Albums.Choice, _main.Choosing);
        Assert.True(_main.IsChoosingInAnAlbum);
        Assert.Equal("1 chosen", _main.Albums.Choice.ChosenSummary);

        // And the library's own choice was not touched by it.
        Assert.False(_main.Gallery.Choice.IsChoosing);
    }

    /// <summary>While choosing, a click on the picture ticks it rather than opening it.</summary>
    [Fact]
    public async Task WhileChoosing_AClickOnTheAlbumsPictureTicksIt()
    {
        await OpenBaliAsync();
        _main.Albums.Choice.ChoosePhoto(Tile(0), extend: false);

        _main.OpenAlbumPhotoCommand.Execute(Tile(1));

        Assert.False(_main.Gallery.IsViewerOpen);
        Assert.Equal(2, _main.Albums.Choice.ChosenCount);
        Assert.True(Tile(1).IsChosen);
    }

    /// <summary>With the mode off, the same click still opens the picture.</summary>
    [Fact]
    public async Task WithTheModeOff_AClickOnTheAlbumsPictureOpensIt()
    {
        await OpenBaliAsync();

        _main.OpenAlbumPhotoCommand.Execute(Tile(0));

        Assert.True(_main.Gallery.IsViewerOpen);
        Assert.Equal(0, _main.Albums.Choice.ChosenCount);
    }

    /// <summary>Going back to the albums lets go of what was ticked in this one.</summary>
    [Fact]
    public async Task LeavingTheAlbumLetsGoOfTheChoice()
    {
        await OpenBaliAsync();
        _main.Albums.Choice.ChooseEverythingCommand.Execute(null);

        _main.Albums.CloseAlbumCommand.Execute(null);

        Assert.False(_main.Albums.Choice.IsChoosing);
        Assert.Equal(0, _main.Albums.Choice.ChosenCount);
        Assert.False(_main.ShowChoosingBar);
        Assert.Null(_main.Choosing);
    }

    /// <summary>A photograph the viewer takes off the album's page leaves the choice too.</summary>
    [Fact]
    public async Task APhotographDroppedFromThePageIsNoLongerChosen()
    {
        await OpenBaliAsync();
        _main.Albums.Choice.ChooseEverythingCommand.Execute(null);

        GalleryTile beach = Tile(0);
        _main.Albums.Photos.Remove(beach);
        ((IViewerGrid)_main.Albums).Dropped(beach);

        Assert.Equal(1, _main.Albums.Choice.ChosenCount);
        Assert.Equal([_temple.Id], _main.Albums.Choice.Ids);
    }

    /// <summary>
    /// Taking a batch out of the album keeps the photographs in the library.
    /// </summary>
    [Fact]
    public async Task TakingOut_EmptiesTheAlbumOfThemAndKeepsThemInTheLibrary()
    {
        await OpenBaliAsync();
        _main.Albums.Choice.ChoosePhoto(Tile(0), extend: false);

        await _main.Albums.TakeOutChosenAsync();

        Assert.Equal([_temple.Id], await MembersAsync(_bali));
        Assert.True(await StillInTheLibraryAsync(_beach.Id));

        // The page caught up, the mode is over, and the line says what happened.
        Assert.Equal(1, _main.Albums.PhotoCount);
        Assert.False(_main.Albums.Choice.IsChoosing);
        Assert.Equal("1 photograph taken out of Bali.", _main.Albums.Status);
    }

    /// <summary>
    /// Filing from an album opens the library's picker, with the album marked
    /// as the one they are in.
    /// </summary>
    [Fact]
    public async Task PuttingInAnAlbum_OffersThePickerWithThisAlbumMarked()
    {
        await OpenBaliAsync();
        _main.Albums.Choice.ChooseEverythingCommand.Execute(null);

        await _main.PutChosenInAlbumCommand.ExecuteAsync(null);

        Assert.True(_main.Gallery.Albums.IsOpenBesideTheBar);
        Assert.Equal("Put these 2 photographs in an album", _main.Gallery.Albums.Prompt);
        Assert.Equal(2, _main.Gallery.PlacingCount);
        Assert.Contains(_main.Gallery.Albums.Choices, choice => choice.IsCurrent && choice.Name == "Bali");

        // The bar has its own way out, worded for many, so the picker's
        // singular one is not offered beside it.
        Assert.False(_main.Gallery.Albums.IsInOne);
    }

    /// <summary>
    /// Filing moves them, and it is the album's page that catches up rather
    /// than the library's.
    /// </summary>
    [Fact]
    public async Task PuttingInAnotherAlbum_MovesThemAndCatchesTheAlbumUp()
    {
        await OpenBaliAsync();
        _main.Albums.Choice.ChooseEverythingCommand.Execute(null);

        string? asked = null;
        _main.Gallery.PlacingChosen += (_, name) => asked = name;

        await _main.PutChosenInAlbumCommand.ExecuteAsync(null);
        await _main.Gallery.Albums.ChooseCommand.ExecuteAsync(
            _main.Gallery.Albums.Choices.Single(choice => choice.Name == "Java"));

        // Handed to the shell, which asks and then writes under the overlay.
        Assert.Equal("Java", asked);
        await _main.Gallery.PlaceChosenAsync("Java");

        Assert.Empty(await MembersAsync(_bali));
        Assert.Equal(
            new[] { _beach.Id, _temple.Id }.Order(),
            (await MembersAsync(_java)).Order());

        Assert.Equal(0, _main.Albums.PhotoCount);
        Assert.False(_main.Albums.Choice.IsChoosing);

        // And the library's choice, which nobody made, is still not on.
        Assert.False(_main.Gallery.Choice.IsChoosing);
    }

    /// <summary>A deletion that removed nothing still ends the mode, and leaves the page alone.</summary>
    [Fact]
    public async Task ADeletionThatRemovedNothingStillEndsTheMode()
    {
        await OpenBaliAsync();
        _main.Albums.Choice.ChooseEverythingCommand.Execute(null);

        await _main.Albums.AfterChosenDeletedAsync(PhotoRemovalResult.Nothing);

        Assert.False(_main.Albums.Choice.IsChoosing);
        Assert.Equal(2, _main.Albums.PhotoCount);
    }

    /// <summary>The album's tiles carry the library's mark and circle.</summary>
    [Fact]
    public void TheAlbumsTilesCarryTheSameCircle()
    {
        string grid = AlbumGrid(Window());

        Assert.Contains("Style=\"{DynamicResource ChosenTick}\"", grid, StringComparison.Ordinal);
        Assert.Contains("Style=\"{DynamicResource ChosenVeil}\"", grid, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPhotographTicked\"", grid, StringComparison.Ordinal);

        // And its last row can still be scrolled clear of the bar.
        Assert.Contains("Margin=\"{Binding WallMargin}\"", grid, StringComparison.Ordinal);
    }

    /// <summary>The bar's way out of an album, only on an album, and quiet.</summary>
    [Fact]
    public void TheBarCarriesAWayOutOfTheAlbumBetweenFilingAndDeleting()
    {
        string window = Window();
        int start = window.IndexOf(
            "Visibility=\"{Binding ShowChoosingBar,", StringComparison.Ordinal);
        int end = window.IndexOf("<!-- Status bar.", start, StringComparison.Ordinal);
        string bar = window[start..end];

        int file = bar.IndexOf("Content=\"Put in album\"", StringComparison.Ordinal);
        int takeOut = bar.IndexOf("Content=\"Take out of album\"", StringComparison.Ordinal);
        int delete = bar.IndexOf(
            "Content=\"{Binding Choosing.DeleteChosenCaption}\"", StringComparison.Ordinal);

        Assert.True(file > 0 && takeOut > file && delete > takeOut, "the three actions are out of order");

        string button = bar[bar.LastIndexOf("<Button ", takeOut, StringComparison.Ordinal)..delete];

        Assert.Contains("SecondaryButton", button, StringComparison.Ordinal);
        Assert.Contains("{Binding IsChoosingInAnAlbum,", button, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnTakeOutChosenClicked\"", button, StringComparison.Ordinal);
    }

    /// <summary>While choosing, the album's own actions are out of use, as the library's strip is.</summary>
    [Fact]
    public void WhileChoosing_TheAlbumsOwnActionsAreOutOfUse()
    {
        int stopped = System.Text.RegularExpressions.Regex.Matches(
            Window(),
            @"IsEnabled=""\{Binding Albums\.Choice\.IsChoosing,\s*"
            + @"Converter=\{StaticResource InverseBool\}\}""").Count;

        // Find photos that fit and Edit, and the panel of what the rule found.
        Assert.Equal(2, stopped);
    }

    /// <summary>
    /// An album's choice survives a trip to the library, and the library's bar
    /// is then the library's.
    /// </summary>
    [Fact]
    public async Task AnAlbumsChoiceHiddenBehindTheLibraryIsNotTheOneOnScreen()
    {
        await OpenBaliAsync();
        _main.Albums.Choice.ChoosePhoto(Tile(0), extend: false);

        _main.SelectedSection =
            _main.TopSections.Single(section => section.Key == ActivitySection.LibraryKey);

        Assert.True(_main.Albums.Choice.IsChoosing);
        Assert.False(_main.IsChoosingInAnAlbum);
        Assert.Same(_main.Gallery.Choice, _main.Choosing);
        Assert.False(_main.ShowChoosingBar);
    }

    /// <summary>
    /// Escape leaves an album's choice only while that album is the wall on
    /// screen, so it never spends itself on ticks nobody can see.
    /// </summary>
    [Fact]
    public void EscapeLeavesAnAlbumsChoiceOnlyWhileItIsOnScreen()
    {
        string shell = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));

        int album = shell.IndexOf("albums.Choice.StopChoosingCommand", StringComparison.Ordinal);
        Assert.True(album > 0, "Escape no longer reaches the album's choice");

        int entry = shell.LastIndexOf("yield return (", album, StringComparison.Ordinal);

        Assert.Contains(
            "_viewModel.IsChoosingInAnAlbum && albums.Choice.IsChoosing",
            shell[entry..album],
            StringComparison.Ordinal);
    }

    private static string Window() =>
        File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    private static string AlbumGrid(string window)
    {
        int start = window.IndexOf("x:Name=\"AlbumPhotoGrid\"", StringComparison.Ordinal);
        Assert.True(start > 0, "the album's grid is not in the window");

        int end = window.IndexOf("</ListBox>", start, StringComparison.Ordinal);
        int opens = window.LastIndexOf("<ListBox ", start, StringComparison.Ordinal);

        return window[opens..end];
    }

    private async Task OpenBaliAsync()
    {
        _main.SelectedSection =
            _main.TopSections.Single(section => section.Key == ActivitySection.AlbumsKey);

        await TestWait.ForAsync(() => _main.Albums.Wall.Count == 2, "the wall to be read");

        _main.Albums.OpenCommand.Execute(_main.Albums.Wall.Single(album => album.Id == _bali));

        await TestWait.ForAsync(() => _main.Albums.PhotoCount == 2, "Bali's photographs to be read");
    }

    private GalleryTile Tile(int at) => _main.Albums.Photos[at];

    private async Task<IReadOnlyList<int>> MembersAsync(int albumId)
    {
        using GalleryDbContext db = Open();
        return await new SqliteAlbumRepository(db).GetMembersAsync(albumId);
    }

    private async Task<bool> StillInTheLibraryAsync(int assetId)
    {
        using GalleryDbContext db = Open();
        return await db.Assets.AnyAsync(asset => asset.Id == assetId);
    }

    private GalleryDbContext Open() =>
        new(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={_database}")
            .Options);

    private static Asset Photo(GalleryDbContext db, string name)
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

        db.Assets.Add(asset);
        db.SaveChanges();
        return asset;
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

        // SQLite keeps a pooled handle on the file after the last context goes.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

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
