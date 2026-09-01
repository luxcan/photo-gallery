using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Albums;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The albums screen once there are shelves: which albums the wall draws, when
/// the band is there, and what going into a collection does.
/// </summary>
/// <remarks>
/// Against a real SQLite file rather than a double, because most of what this
/// asserts is a filter over what the repository returned and a double would be
/// asserting the filter against itself.
/// </remarks>
public sealed class CollectionsScreenTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly ServiceProvider _services;
    private readonly IAlbumRepository _albumStore;
    private readonly ICollectionRepository _shelves;
    private readonly AlbumsViewModel _albums;

    public CollectionsScreenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-shelf-screen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _albumStore = new SqliteAlbumRepository(_db);
        _shelves = new SqliteCollectionRepository(_db);

        _services = new ServiceCollection()
            .AddSingleton(_albumStore)
            .AddSingleton(_shelves)
            .BuildServiceProvider();

        _albums = new AlbumsViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    [Fact]
    public async Task TheBandIsAbsentUntilThereIsACollection()
    {
        await _albumStore.CreateAsync("Genting");
        await _albums.ReloadAsync();

        Assert.False(_albums.Collections.HasAny);
        Assert.False(_albums.ShowingTheBand);
        Assert.True(_albums.ShowingTheStrip);
    }

    [Fact]
    public async Task TheWallShowsOnlyTheAlbumsOnNoShelf()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        await _albumStore.CreateAsync("Chingay");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);

        await _albums.ReloadAsync();

        Assert.Equal(2, _albums.Mine.Count);
        Assert.Equal("Chingay", Assert.Single(_albums.Wall).Name);
        Assert.True(_albums.ShowingTheBand);
    }

    [Fact]
    public async Task OpeningACollectionShowsTheAlbumsOnIt()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        await _albumStore.CreateAsync("Chingay");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();

        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());

        Assert.Equal("Genting", Assert.Single(_albums.Wall).Name);
        Assert.True(_albums.ShowingOneCollection);
        Assert.False(_albums.ShowingTheStrip);
        Assert.False(_albums.ShowingTheBand);
    }

    [Fact]
    public async Task ComingOutOfACollectionShowsTheLooseAlbumsAgain()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        await _albumStore.CreateAsync("Chingay");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();

        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        _albums.Collections.CloseCommand.Execute(null);

        Assert.Equal("Chingay", Assert.Single(_albums.Wall).Name);
        Assert.True(_albums.ShowingTheStrip);
    }

    /// <summary>
    /// There is no foreign key behind that column, so this is the rule that
    /// keeps a dangling one from being an album nobody can find.
    /// </summary>
    [Fact]
    public async Task AnAlbumOnAShelfNobodyHasHeardOfIsOnTheWall()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        Album album = await _db.Albums.SingleAsync(a => a.Id == genting);
        album.CollectionId = 404;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _albums.ReloadAsync();

        Assert.Equal("Genting", Assert.Single(_albums.Wall).Name);
    }

    [Fact]
    public async Task GoingIntoACollectionClosesTheOpenAlbum()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();

        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        _albums.Selected = _albums.Wall.Single();
        Assert.True(_albums.HasSelected);

        _albums.Collections.CloseCommand.Execute(null);

        Assert.False(_albums.HasSelected);
    }

    [Fact]
    public async Task MakingOneOpensItSoItCanBeFilled()
    {
        await _albums.ReloadAsync();

        _albums.Collections.StartCreatingCommand.Execute(null);
        _albums.Collections.TypedName = "Holiday";
        await _albums.Collections.SaveNameCommand.ExecuteAsync(null);

        Assert.False(_albums.Collections.IsNaming);
        Assert.Equal("Holiday", _albums.Collections.OpenName);
        Assert.True(_albums.ShowingOneCollection);
    }

    [Fact]
    public async Task ANameThatIsAlreadyTakenIsRefusedBeforeItIsTried()
    {
        await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();

        _albums.Collections.StartCreatingCommand.Execute(null);
        _albums.Collections.TypedName = "holiday";

        Assert.True(_albums.Collections.HasNameProblem);
        Assert.False(_albums.Collections.SaveNameCommand.CanExecute(null));
    }

    [Fact]
    public async Task RenamingDoesNotCollideWithTheShelfBeingRenamed()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == holiday));

        _albums.Collections.StartRenamingCommand.Execute(null);

        Assert.Equal("Holiday", _albums.Collections.TypedName);
        Assert.False(_albums.Collections.HasNameProblem);
        Assert.True(_albums.Collections.SaveNameCommand.CanExecute(null));
    }

    /// <summary>
    /// Every album, including the ones on another shelf, and the line says which
    /// shelf that is.
    /// </summary>
    /// <remarks>
    /// Offering only the loose ones would make moving an album between two
    /// collections a trip to the first to untick it and a trip back - which is
    /// the procedure a tick list exists to avoid.
    /// </remarks>
    [Fact]
    public async Task TheListOffersEveryAlbumAndSaysWhereEachOneIs()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int chingay = await _albumStore.CreateAsync("Chingay");
        int bali = await _albumStore.CreateAsync("Bali");
        int holiday = await _shelves.CreateAsync("Holiday");
        int weekends = await _shelves.CreateAsync("Weekends");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _shelves.SetAlbumsAsync(weekends, [bali]);

        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == holiday));
        await _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        Assert.True(_albums.Collections.IsPicking);
        Assert.Equal(
            ["Bali", "Chingay", "Genting"],
            _albums.Collections.Choices.Select(choice => choice.Name).Order());
        Assert.True(_albums.Collections.Choices.Single(c => c.Id == genting).IsChosen);
        Assert.False(_albums.Collections.Choices.Single(c => c.Id == chingay).IsChosen);
        Assert.Contains(
            "on Weekends",
            _albums.Collections.Choices.Single(c => c.Id == bali).Caption,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule an album already follows for a photograph, one level up: it is
    /// on one collection, so joining this one is leaving that one, and the app
    /// says which rather than enforcing a rule nobody asked about in silence.
    /// </summary>
    [Fact]
    public async Task TickingAnAlbumFromAnotherShelfMovesItAndSaysWhichItLeft()
    {
        int bali = await _albumStore.CreateAsync("Bali");
        int holiday = await _shelves.CreateAsync("Holiday");
        int weekends = await _shelves.CreateAsync("Weekends");
        await _shelves.SetAlbumsAsync(weekends, [bali]);

        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == holiday));
        await _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        _albums.Collections.Choices.Single(c => c.Id == bali).IsChosen = true;
        await _albums.Collections.SavePickCommand.ExecuteAsync(null);

        Assert.Contains("Taken out of Weekends", _albums.Status, StringComparison.Ordinal);
        Assert.Equal("Bali", Assert.Single(_albums.Wall).Name);
        Assert.Equal(
            0, _albums.Collections.All.Single(item => item.Id == weekends).Summary.AlbumCount);
    }

    [Fact]
    public async Task SavingTheListMovesTheAlbumsAndSaysSo()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        await _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        _albums.Collections.Choices.Single(c => c.Id == genting).IsChosen = true;
        await _albums.Collections.SavePickCommand.ExecuteAsync(null);

        Assert.False(_albums.Collections.IsPicking);
        Assert.Equal("Genting", Assert.Single(_albums.Wall).Name);
        Assert.Contains("1 album added", _albums.Status, StringComparison.Ordinal);
        Assert.Equal(holiday, _albums.Collections.Open!.Id);
    }

    [Fact]
    public async Task TickingASuggestionSaysItWasKept()
    {
        Album proposed = Suggested("March 2019");
        await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        await _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        _albums.Collections.Choices.Single(c => c.Id == proposed.Id).IsChosen = true;
        await _albums.Collections.SavePickCommand.ExecuteAsync(null);

        Assert.Contains("now yours to keep", _albums.Status, StringComparison.Ordinal);
        Assert.Empty(_albums.Suggested);
    }

    [Fact]
    public async Task AnEmptyShelfSaysHowToFillIt()
    {
        await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());

        Assert.True(_albums.HasNone);
        Assert.Contains("Add albums", _albums.EmptyMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty wall with albums behind it must say why, or it reads as the
    /// albums having gone.
    /// </summary>
    [Fact]
    public async Task AWallEmptyBecauseEverythingIsShelvedSaysThat()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();

        Assert.True(_albums.HasNone);
        Assert.Contains("is on a collection", _albums.EmptyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovingAShelfPutsItsAlbumsBackOnTheWall()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());

        await _albums.Collections.DeleteCommand.ExecuteAsync(null);

        Assert.False(_albums.Collections.HasAny);
        Assert.Equal("Genting", Assert.Single(_albums.Wall).Name);
        Assert.True(_albums.ShowingTheStrip);
        Assert.Contains("back on the wall", _albums.Status, StringComparison.Ordinal);
    }

    /// <summary>The suggestions tab is unchanged, and never shows a band.</summary>
    [Fact]
    public async Task TheSuggestedTabHasNoBand()
    {
        Suggested("March 2019");
        await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();

        _albums.ShowMine = false;

        Assert.False(_albums.ShowingTheBand);
        Assert.Equal("March 2019", Assert.Single(_albums.Showing).Name);
    }

    private Album Suggested(string name)
    {
        var album = new Album
        {
            Name = name,
            StartUtc = new DateTime(2019, 3, 20, 9, 0, 0, DateTimeKind.Unspecified),
            EndUtc = new DateTime(2019, 3, 20, 18, 0, 0, DateTimeKind.Unspecified),
            Kind = AlbumKind.Day,
            Origin = AlbumOrigin.Proposed,
            ProposalKey = "2019-03-20..2019-03-20",
            BuiltUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        _db.Albums.Add(album);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return album;
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
            // A locked temporary folder is not a failed test.
        }
    }
}
