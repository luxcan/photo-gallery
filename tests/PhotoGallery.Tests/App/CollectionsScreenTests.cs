using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Albums;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.People;
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
    private readonly GatedAlbums _reads;
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

        // Somebody for the rule half of the album panel to find. An empty
        // directory reads the same whether it was asked for or never reached, so
        // a library with nobody in it cannot tell a panel that opened from one
        // that gave up half way and left the reason in Status.
        _db.People.Add(new Person { DisplayName = "Aunt Mei" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _albumStore = new SqliteAlbumRepository(_db);
        _shelves = new SqliteCollectionRepository(_db);
        _reads = new GatedAlbums(_albumStore);

        // The album panel reads the people and the places as well as the rule,
        // so a container answering only the two repositories opens half a panel
        // and every assertion here is about the half that was already filled.
        //
        // All four are singletons over the one context above, as the sibling
        // fixtures register theirs and unlike production, which hands every
        // scope a context of its own. What that costs is one shape of test: two
        // reads genuinely overlapping would throw on a shared context where
        // production would answer, so the gate below holds a read in front of
        // the context rather than inside it, and a test that needs two real
        // reads at once belongs in a fixture that scopes.
        _services = new ServiceCollection()
            .AddSingleton<IAlbumRepository>(_reads)
            .AddSingleton(_shelves)
            .AddSingleton<IPeopleReader>(new SqlitePeopleReader(_db))
            .AddSingleton<IPlaceReader>(new SqlitePlaceReader(_db))
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
    /// Opening the naming panel tells the screen to read the caution line and
    /// the Save button again, even when the box already holds the answer.
    /// </summary>
    /// <remarks>
    /// The panel keeps whatever was last typed in it, so opening it on a shelf
    /// whose name is already in the box writes the string the box already
    /// holds - and an assignment that changes nothing announces nothing. The
    /// panel then came up refusing the name of the very shelf it had been
    /// opened to rename, with Save dead until a key was pressed. The values
    /// were always right, because all three are getters that recompute on every
    /// read; what was missing was the screen being told to read them, which is
    /// why this watches what was announced rather than what it now says.
    /// </remarks>
    [Fact]
    public async Task RenamingAfterACancelledCollisionSaysTheNameIsFreeAgain()
    {
        int beach = await _shelves.CreateAsync("Beach");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();

        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == beach));
        _albums.Collections.StartRenamingCommand.Execute(null);
        _albums.Collections.TypedName = "Holiday";
        Assert.True(_albums.Collections.HasNameProblem);
        _albums.Collections.CancelNamingCommand.Execute(null);

        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == holiday));

        List<string> announced = [];
        bool askedAgain = false;
        _albums.Collections.PropertyChanged +=
            (_, e) => announced.Add(e.PropertyName ?? string.Empty);
        _albums.Collections.SaveNameCommand.CanExecuteChanged += (_, _) => askedAgain = true;

        _albums.Collections.StartRenamingCommand.Execute(null);

        Assert.False(_albums.Collections.HasNameProblem);
        Assert.True(_albums.Collections.SaveNameCommand.CanExecute(null));
        Assert.Contains(nameof(CollectionsViewModel.NameProblem), announced);
        Assert.Contains(nameof(CollectionsViewModel.HasNameProblem), announced);
        Assert.True(askedAgain);
    }

    /// <summary>
    /// The same hole seeded by New collection rather than by a rename, which is
    /// the half a test of renaming alone would miss.
    /// </summary>
    /// <remarks>
    /// New leaves the box holding the name that was refused and the shelf being
    /// named set to none. Renaming the shelf that name belongs to then moves
    /// the shelf from none to that one while the box does not move at all.
    /// </remarks>
    [Fact]
    public async Task RenamingAfterACancelledNewCollectionSaysTheNameIsFreeAgain()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();

        _albums.Collections.StartCreatingCommand.Execute(null);
        _albums.Collections.TypedName = "Holiday";
        Assert.True(_albums.Collections.HasNameProblem);
        _albums.Collections.CancelNamingCommand.Execute(null);

        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == holiday));

        List<string> announced = [];
        bool askedAgain = false;
        _albums.Collections.PropertyChanged +=
            (_, e) => announced.Add(e.PropertyName ?? string.Empty);
        _albums.Collections.SaveNameCommand.CanExecuteChanged += (_, _) => askedAgain = true;

        _albums.Collections.StartRenamingCommand.Execute(null);

        Assert.False(_albums.Collections.HasNameProblem);
        Assert.True(_albums.Collections.SaveNameCommand.CanExecute(null));
        Assert.Contains(nameof(CollectionsViewModel.NameProblem), announced);
        Assert.Contains(nameof(CollectionsViewModel.HasNameProblem), announced);
        Assert.True(askedAgain);
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
    /// Only the shelf still open when the read comes back may raise the list.
    /// </summary>
    /// <remarks>
    /// Nothing covers the screen while it reads, so the back chevron beside the
    /// name stays live. A list raised for a shelf that has been left behind
    /// stands over the top level with a blank heading and a Save that wants an
    /// open shelf, so it can never light up and the only way out is the chevron
    /// underneath it.
    /// </remarks>
    [Fact]
    public async Task GoingBackWhileTheListIsBeingReadRaisesNoList()
    {
        await _albumStore.CreateAsync("Genting");
        await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());

        var held = new TaskCompletionSource();
        _reads.Held = held;
        Task picking = _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        _albums.Collections.CloseCommand.Execute(null);
        held.SetResult();
        await picking;

        Assert.False(_albums.Collections.IsPicking);
        Assert.Empty(_albums.Collections.OpenName);
        Assert.True(_albums.Collections.IsIdle);
    }

    /// <summary>
    /// And a list read for one shelf is not raised over another, which is the
    /// worse half of the same race.
    /// </summary>
    /// <remarks>
    /// The heading names the shelf that is open now and the ticks describe the
    /// one left behind, so saving it would empty this shelf on to that one -
    /// and the list would look right while it did.
    /// </remarks>
    [Fact]
    public async Task SwitchingShelfWhileTheListIsBeingReadRaisesNoList()
    {
        int bali = await _albumStore.CreateAsync("Bali");
        int holiday = await _shelves.CreateAsync("Holiday");
        int weekends = await _shelves.CreateAsync("Weekends");
        await _shelves.SetAlbumsAsync(holiday, [bali]);
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == holiday));

        var held = new TaskCompletionSource();
        _reads.Held = held;
        Task picking = _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        _albums.Collections.CloseCommand.Execute(null);
        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == weekends));
        held.SetResult();
        await picking;

        Assert.False(_albums.Collections.IsPicking);
        Assert.Equal("Weekends", _albums.Collections.OpenName);
        Assert.True(_albums.Collections.IsIdle);
    }

    /// <summary>
    /// A library that cannot be read is said on this screen rather than left to
    /// close the app.
    /// </summary>
    /// <remarks>
    /// The catch here was written out for a file read, and the library is a
    /// database too: SqliteException derives from DbException, which none of
    /// the three types it named matched. What is watched is the sentence this
    /// screen raises rather than the status line, because the albums screen
    /// reports the same fault on the same read straight afterwards and the two
    /// sentences read alike.
    /// </remarks>
    [Fact]
    public async Task AlbumsThatCannotBeReadAreSaidOnTheScreenRatherThanClosingTheApp()
    {
        await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());

        List<string> announced = [];
        _albums.Collections.Changed += (_, sentence) => announced.Add(sentence);
        _reads.ReadFails = new SqliteException("database is locked", 5);

        await _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        string said = Assert.Single(announced);
        Assert.Contains("could not be read", said, StringComparison.Ordinal);
        Assert.Contains("database is locked", said, StringComparison.Ordinal);
        Assert.False(_albums.Collections.IsPicking);
        Assert.True(_albums.Collections.IsIdle);
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

    /// <summary>
    /// A suggestion already standing on the shelf is kept by this save too, and
    /// a shelf that kept one is not an unchanged shelf.
    /// </summary>
    /// <remarks>
    /// The test above ticks a proposal that is not on the shelf yet, so an
    /// album joins and the sentence gets written on the way past that. This is
    /// the case the wording is really about: nothing joins, nothing leaves, and
    /// the save is what accepts the album standing there - which is a change to
    /// somebody's library that outlives this screen, since no later pass may
    /// rewrite an album that was kept.
    /// </remarks>
    [Fact]
    public async Task KeepingASuggestionAlreadyOnTheShelfIsNotAnUnchangedShelf()
    {
        Album proposed = Suggested("March 2019");
        int holiday = await _shelves.CreateAsync("Holiday");

        // On a shelf while still a suggestion, which is the state the kept
        // count exists for: an album can reach a shelf before anything has
        // accepted it.
        Album standing = await _db.Albums.SingleAsync(a => a.Id == proposed.Id);
        standing.CollectionId = holiday;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        await _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        // Ticked already, because it is on the shelf. Saving the list exactly
        // as it came up is the whole of this.
        Assert.True(_albums.Collections.Choices.Single(c => c.Id == proposed.Id).IsChosen);
        await _albums.Collections.SavePickCommand.ExecuteAsync(null);

        Assert.DoesNotContain("unchanged", _albums.Status, StringComparison.Ordinal);
        Assert.Contains("now yours to keep", _albums.Status, StringComparison.Ordinal);
        Assert.Equal(
            AlbumOrigin.Accepted,
            await _db.Albums.Where(a => a.Id == proposed.Id)
                .Select(a => a.Origin).SingleAsync());
    }

    /// <summary>A save that only takes an album off reads as English.</summary>
    /// <remarks>
    /// The sentence used to be a list of clauses with one ending hung on all of
    /// them, and "added" and "taken off" do not take the same preposition - so
    /// a removal on its own came out as 1 taken off to "Holiday", naming the
    /// shelf the album had just left as the one it went to.
    /// </remarks>
    [Fact]
    public async Task ASaveThatOnlyTakesAnAlbumOffSaysSoInEnglish()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        await _albums.Collections.StartPickingCommand.ExecuteAsync(null);

        _albums.Collections.Choices.Single(c => c.Id == genting).IsChosen = false;
        await _albums.Collections.SavePickCommand.ExecuteAsync(null);

        Assert.Contains(
            "1 album taken off \"Holiday\".", _albums.Status, StringComparison.Ordinal);
        Assert.Null(
            await _db.Albums.Where(a => a.Id == genting)
                .Select(a => a.CollectionId).SingleAsync());
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

    /// <summary>
    /// Remove asks first, and the shelf can be gone by the time it is answered.
    /// </summary>
    /// <remarks>
    /// The question is a modal window, which pumps messages for as long as it
    /// is up: a reload finishing behind it re-points the open shelf, and at
    /// nothing when a second copy of the app on the same library has taken it
    /// away. Remove is a Click handler with a question in front of it rather
    /// than a command binding, so it runs whether or not CanExecute still
    /// agrees - which is why the assertion below that it does not is not the
    /// end of the test.
    /// </remarks>
    [Fact]
    public async Task RemovingAShelfThatWentAwayWhileTheQuestionWasUpDoesNothing()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());

        await _shelves.DeleteAsync(holiday);
        await _albums.Collections.ReloadAsync();

        Assert.Null(_albums.Collections.Open);
        Assert.False(_albums.Collections.DeleteCommand.CanExecute(null));

        await _albums.Collections.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_albums.Status);
        Assert.True(_albums.Collections.IsIdle);
    }

    /// <summary>
    /// And the other three answers to the open shelf, which the same reload can
    /// leave without one.
    /// </summary>
    /// <remarks>
    /// These are command bindings rather than Click handlers, so the shipped
    /// buttons go dead with the shelf - but a command asked to run is run, and
    /// each body reads the shelf again for the same reason Remove does.
    /// </remarks>
    [Fact]
    public async Task TheOtherAnswersToAShelfThatWentAwayDoNothing()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());

        await _shelves.DeleteAsync(holiday);
        await _albums.Collections.ReloadAsync();
        Assert.Null(_albums.Collections.Open);

        _albums.Collections.StartRenamingCommand.Execute(null);
        await _albums.Collections.StartPickingCommand.ExecuteAsync(null);
        await _albums.Collections.SavePickCommand.ExecuteAsync(null);

        Assert.False(_albums.Collections.IsNaming);
        Assert.False(_albums.Collections.IsPicking);
        Assert.Empty(_albums.Status);
        Assert.True(_albums.Collections.IsIdle);
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

    /// <summary>
    /// An empty shelf says it is empty, rather than drawing four blank tiles.
    /// </summary>
    /// <remarks>
    /// The first version of this band gave a collection the album card's own
    /// 180px cover. On dark, that placeholder is the same colour as the page
    /// behind it, so an empty shelf was a hole the size of a photograph with a
    /// name adrift underneath - which is what it looked like in the app.
    /// </remarks>
    [Fact]
    public async Task AnEmptyShelfIsDrawnAsSomewhereToPutSomething()
    {
        await _shelves.CreateAsync("Chingay");
        await _albums.ReloadAsync();

        CollectionItem shelf = Assert.Single(_albums.Collections.All);
        Assert.False(shelf.HasAlbums);
        Assert.Equal("Empty - add albums", shelf.Caption);
        Assert.Equal(CollectionItem.MosaicTiles, shelf.Covers.Count);
        Assert.All(shelf.Covers, Assert.Null);
    }

    [Fact]
    public async Task AFilledShelfCountsItsAlbumsAndItsPhotographs()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();

        CollectionItem shelf = Assert.Single(_albums.Collections.All);
        Assert.True(shelf.HasAlbums);
        Assert.Equal("1 album · 0 photos", shelf.Caption);
        Assert.Equal("1 shelf", _albums.Collections.ShelfCount);
    }

    /// <summary>
    /// The band draws a row with a mosaic, and the wall draws album cards. The
    /// shape is the whole of what tells a shelf from an album, so it is worth a
    /// test that reads the markup.
    /// </summary>
    [Fact]
    public void TheBandIsNotDrawnWithAnAlbumCard()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        // The whole binding, not its opening: Albums.ShowingTheStrip starts with
        // Albums.Showing and sits above both of these.
        int band = markup.IndexOf("{Binding Albums.Collections.All}", StringComparison.Ordinal);
        int wall = markup.IndexOf("{Binding Albums.Showing}", StringComparison.Ordinal);
        Assert.InRange(band, 1, wall);

        string inTheBand = markup[band..wall];
        Assert.Contains("ShelfCard", inTheBand, StringComparison.Ordinal);
        Assert.Contains("{Binding Covers}", inTheBand, StringComparison.Ordinal);
        Assert.DoesNotContain("AlbumCard", inTheBand, StringComparison.Ordinal);

        // And the wall still draws album cards, so this cannot pass by the band
        // and the wall having swapped places.
        Assert.Contains("AlbumCard", markup[wall..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAlbumPanelOffersEveryShelfAndNone()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.CreateAsync("Weekends");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();

        // Inside the shelf, because that is where an album standing on one is.
        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == holiday));
        _albums.Selected = _albums.Wall.Single();
        await _albums.EditCommand.ExecuteAsync(null);

        Assert.Equal(
            ["Not on a collection", "Holiday", "Weekends"],
            _albums.CollectionOptions.Select(option => option.Name));
        Assert.Equal("Holiday", _albums.EditedCollection.Name);

        // The rule half is read first and the shelf half above is filled only
        // once it has arrived, so what is left to say is that what arrived was
        // this library's directory rather than an empty one.
        Assert.Equal("Aunt Mei", Assert.Single(_albums.People).Name);
    }

    /// <summary>
    /// The other direction of the tick list: from the album, choosing a shelf
    /// moves it, and the panel says which one it came off.
    /// </summary>
    [Fact]
    public async Task ChoosingACollectionMovesTheAlbumAndSaysWhichItLeft()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        int weekends = await _shelves.CreateAsync("Weekends");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();

        _albums.Collections.OpenShelfCommand.Execute(
            _albums.Collections.All.Single(item => item.Id == holiday));
        _albums.Selected = _albums.Wall.Single();
        await _albums.EditCommand.ExecuteAsync(null);

        _albums.EditedCollection =
            _albums.CollectionOptions.Single(option => option.Id == weekends);
        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.False(_albums.IsEditing);
        Assert.Contains("Taken off \"Holiday\"", _albums.Status, StringComparison.Ordinal);
        Assert.Equal(
            weekends,
            await _db.Albums.Where(a => a.Id == genting)
                .Select(a => a.CollectionId).SingleAsync());
    }

    [Fact]
    public async Task TakingAnAlbumOffEveryShelfPutsItBackOnTheWall()
    {
        int genting = await _albumStore.CreateAsync("Genting");
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _albums.ReloadAsync();

        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());
        _albums.Selected = _albums.Wall.Single();
        await _albums.EditCommand.ExecuteAsync(null);

        _albums.EditedCollection = _albums.CollectionOptions.First();
        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Null(
            await _db.Albums.Where(a => a.Id == genting)
                .Select(a => a.CollectionId).SingleAsync());
    }

    /// <summary>
    /// New album, pressed from inside a shelf, means an album on that shelf.
    /// </summary>
    /// <remarks>
    /// The alternative is making one and immediately going to find it on the
    /// wall outside, which is the procedure the whole screen is arranged to
    /// avoid.
    /// </remarks>
    [Fact]
    public async Task AnAlbumMadeInsideACollectionLandsOnIt()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();
        _albums.Collections.OpenShelfCommand.Execute(_albums.Collections.All.Single());

        await _albums.StartCreatingCommand.ExecuteAsync(null);

        Assert.Equal("Holiday", _albums.EditedCollection.Name);

        // The other way into the panel, which reads the rule before it fills
        // the collection in the same order.
        Assert.Equal("Aunt Mei", Assert.Single(_albums.People).Name);

        _albums.EditedName = "Genting";
        await _albums.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            holiday,
            await _db.Albums.Where(a => a.Name == "Genting")
                .Select(a => a.CollectionId).SingleAsync());
    }

    /// <summary>
    /// And the header of an open collection offers New album, so the album that
    /// lands on the shelf can be made from inside one.
    /// </summary>
    /// <remarks>
    /// The test above passes on a screen with no way to reach it: it opens the
    /// shelf and calls the command itself, and no view model can be asked
    /// whether a button is bound to it. The strip carries the same command and
    /// the strip is hidden while a collection is open, so the header is the only
    /// place this button can be.
    /// </remarks>
    [Fact]
    public void TheOpenCollectionOffersNewAlbum()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        int header = markup.IndexOf(
            "{Binding Albums.ShowingOneCollection,", StringComparison.Ordinal);
        int band = markup.IndexOf(
            "{Binding Albums.ShowingTheBand,", StringComparison.Ordinal);
        Assert.InRange(header, 1, band);

        string inTheHeader = markup[header..band];
        Assert.Contains(
            "Command=\"{Binding Albums.StartCreatingCommand}\"",
            inTheHeader,
            StringComparison.Ordinal);

        // Not New collection, which is one segment away from it and would draw
        // and do nothing in its place.
        Assert.DoesNotContain(
            "Albums.Collections.StartCreatingCommand",
            inTheHeader,
            StringComparison.Ordinal);

        // And the strip keeps its own, so this cannot pass by the button having
        // moved off the top level.
        Assert.Contains(
            "Command=\"{Binding Albums.StartCreatingCommand}\"",
            markup[..header],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAlbumMadeAtTheTopLevelIsOnNoCollection()
    {
        await _shelves.CreateAsync("Holiday");
        await _albums.ReloadAsync();

        await _albums.StartCreatingCommand.ExecuteAsync(null);

        // That a panel opened at all, before what it defaulted to. "Not on a
        // collection" is also what the field holds before anything opens, so on
        // its own it cannot tell a panel that defaulted correctly from one that
        // gave up half way and left the reason in Status.
        Assert.True(_albums.IsEditing);
        Assert.Empty(_albums.Status);
        Assert.Equal("Not on a collection", _albums.EditedCollection.Name);
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

    /// <summary>
    /// The real albums, with the one read this screen makes held open or
    /// refused.
    /// </summary>
    /// <remarks>
    /// A decorator rather than a double: everything else these tests do is a
    /// real write to a real library, and only the timing of that read is in
    /// question. The gate sits in front of the inner call rather than inside
    /// it, so a held read is a call that has not reached the context yet -
    /// which is what lets this fixture share one.
    /// </remarks>
    private sealed class GatedAlbums : IAlbumRepository
    {
        private readonly IAlbumRepository _inner;

        public GatedAlbums(IAlbumRepository inner) => _inner = inner;

        /// <summary>Holds the read of the albums open, so a late one can be tested.</summary>
        public TaskCompletionSource? Held { get; set; }

        /// <summary>What that read throws, where it is to fail.</summary>
        public Exception? ReadFails { get; set; }

        public async Task<IReadOnlyList<AlbumSummary>> GetAsync(
            CancellationToken cancellationToken = default)
        {
            if (Held is not null)
            {
                await Held.Task.ConfigureAwait(false);
            }

            if (ReadFails is not null)
            {
                throw ReadFails;
            }

            return await _inner.GetAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<IReadOnlyList<DatedPhoto>> GetCandidatesAsync(
            CancellationToken cancellationToken = default) =>
            _inner.GetCandidatesAsync(cancellationToken);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> GetRejectionsAsync(
            CancellationToken cancellationToken = default) =>
            _inner.GetRejectionsAsync(cancellationToken);

        public Task<int> SaveProposalsAsync(
            IReadOnlyList<ProposedAlbum> proposals,
            CancellationToken cancellationToken = default) =>
            _inner.SaveProposalsAsync(proposals, cancellationToken);

        public Task<AlbumSummary?> FindForAssetAsync(
            int assetId, CancellationToken cancellationToken = default) =>
            _inner.FindForAssetAsync(assetId, cancellationToken);

        public Task<IReadOnlyList<int>> GetMembersAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            _inner.GetMembersAsync(albumId, cancellationToken);

        public Task<int> CreateAsync(string name, CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(name, cancellationToken);

        public Task<AlbumRule> GetRuleAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            _inner.GetRuleAsync(albumId, cancellationToken);

        public Task SetRuleAsync(
            int albumId, AlbumRule rule, CancellationToken cancellationToken = default) =>
            _inner.SetRuleAsync(albumId, rule, cancellationToken);

        public Task<IReadOnlyList<int>> SuggestAsync(
            int albumId, CancellationToken cancellationToken = default) =>
            _inner.SuggestAsync(albumId, cancellationToken);

        public Task AcceptAsync(int albumId, CancellationToken cancellationToken = default) =>
            _inner.AcceptAsync(albumId, cancellationToken);

        public Task DismissAsync(int albumId, CancellationToken cancellationToken = default) =>
            _inner.DismissAsync(albumId, cancellationToken);

        public Task RenameAsync(
            int albumId, string name, CancellationToken cancellationToken = default) =>
            _inner.RenameAsync(albumId, name, cancellationToken);

        public Task DeleteAsync(int albumId, CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(albumId, cancellationToken);

        public Task<AlbumAddResult> AddAsync(
            int albumId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            _inner.AddAsync(albumId, assetIds, cancellationToken);

        public Task RemoveAsync(
            int albumId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            _inner.RemoveAsync(albumId, assetIds, cancellationToken);

        public Task RefuseAsync(
            int albumId,
            IReadOnlyList<int> assetIds,
            CancellationToken cancellationToken = default) =>
            _inner.RefuseAsync(albumId, assetIds, cancellationToken);

        public Task<bool> SetCoverAsync(
            int albumId, int assetId, CancellationToken cancellationToken = default) =>
            _inner.SetCoverAsync(albumId, assetId, cancellationToken);
    }
}
