using Microsoft.EntityFrameworkCore;
using PhotoGallery.App.Albums;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What the picker offers a selection, and the question asked before one moves.
/// </summary>
/// <remarks>
/// The list of albums arrives in the order the albums wall wants - by the span
/// of the photographs in them - and that is no order at all for filing: on the
/// first real library it is three hundred long, 284 of them proposals nobody has
/// answered, and the holiday you added to last night sits somewhere in the
/// middle of it.
/// </remarks>
public sealed class FilingASelectionTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly GalleryDbContext _db;

    public FilingASelectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-filing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();
    }

    /// <summary>
    /// An album says when a photograph was last put in it.
    /// </summary>
    /// <remarks>
    /// Against a real file, because the whole value of this is that it works
    /// backwards: AddedUtc has been written on every insert since the column
    /// existed, so nothing had to be recorded and every library already knows.
    /// </remarks>
    [Fact]
    public async Task AnAlbumSaysWhenSomethingWasLastPutInIt()
    {
        int first = Add("first.jpg");
        int second = Add("second.jpg");

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [first, second]);

        // The two moments are written rather than raced for. Two inserts a
        // millisecond apart would make "the later one" a coin toss on a fast
        // machine, and a test that passes because the clock ticked is a test
        // that fails when it does not.
        await StampAsync(album, first, Day);
        await StampAsync(album, second, Day.AddDays(2));

        Assert.Equal(Day.AddDays(2), Single(await Repository().GetAsync(), "Sunday").LastAddedUtc);
    }

    /// <summary>Puts a moment on one membership row.</summary>
    private async Task StampAsync(int albumId, int assetId, DateTime when)
    {
        await _db.AlbumMembers
            .Where(member => member.AlbumId == albumId && member.AssetId == assetId)
            .ExecuteUpdateAsync(set => set.SetProperty(member => member.AddedUtc, when));

        _db.ChangeTracker.Clear();
    }

    /// <summary>
    /// An album holding nothing still counts as used, because making it was
    /// using it.
    /// </summary>
    /// <remarks>
    /// The case recency read off the memberships alone got wrong: an album made
    /// a moment ago had no moment at all and sorted below every album that did -
    /// which is exactly backwards, since one made in order to file into is the
    /// one in hand.
    /// </remarks>
    [Fact]
    public async Task AnAlbumJustMadeCountsAsUsedEvenHoldingNothing()
    {
        DateTime before = DateTime.UtcNow.AddSeconds(-1);

        await Repository().CreateAsync("Empty");

        AlbumSummary summary = Single(await Repository().GetAsync(), "Empty");

        Assert.Null(summary.LastAddedUtc);
        Assert.NotNull(summary.LastUsedUtc);
        Assert.True(summary.LastUsedUtc >= before, "the making of it was not recorded");
    }

    /// <summary>
    /// The picker offers your own albums, the latest filed into at the top.
    /// </summary>
    /// <remarks>
    /// Recency is measured over albums you made or kept and only those, and that
    /// is the point rather than a detail. A rebuild writes every proposal's
    /// memberships in one go: on the first real library 284 of 291 albums share
    /// a last-added moment to the millisecond, so ordering everything by it
    /// would hoist whatever the last scan happened to touch and call it recent.
    /// </remarks>
    [Fact]
    public void ThePickerOffersYourOwnAlbumsWithTheLatestOnTop()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [
                Proposed("Nine days with Shirley", Day.AddDays(9)),
                Made("Sentosa", Day.AddDays(1)),
                Proposed("Fourteen days with Jia Le", Day.AddDays(9)),
                Made("Our Books", Day.AddDays(4)),
                Made("Never used", null),
            ],
            0,
            "Put these 5 photographs in an album",
            "hint");

        Assert.Equal(
            ["Our Books", "Sentosa", "Never used"],
            picker.Choices.Select(choice => choice.Name));
    }

    /// <summary>
    /// The app's own suggestions are not offered to file into.
    /// </summary>
    /// <remarks>
    /// Not tidiness. A rebuild may change or remove a proposal - that is what
    /// makes it a proposal - and putting a photograph in one does not make it
    /// yours, so filing into a suggestion can be quietly undone by the next
    /// scan. That is the one thing filing must never do.
    ///
    /// <para>They are also nearly all of them, and they are named after the
    /// places and dates they were grouped from: typing "tam" into a picker that
    /// offered them found forty-three albums, four of which were called
    /// "Tampines Estate" - which is how this came back as the picker having gone
    /// off and found locations.</para>
    /// </remarks>
    [Fact]
    public void TheAppsOwnSuggestionsAreNotOfferedToFileInto()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [
                Proposed("Tampines Estate", Day),
                Proposed("Tampines New Town", Day),
                Made("Zoo 2026", Day),
            ],
            0,
            "Put this photograph in an album",
            "hint");

        Assert.Equal(["Zoo 2026"], picker.Choices.Select(choice => choice.Name));

        // And the count says what it is counting, rather than three hundred.
        picker.Typed = "Tam";

        Assert.Empty(picker.Choices);
        Assert.Equal("0 of 1", picker.Narrowed);

        // The name is free to be given, because nothing offered carries it.
        Assert.True(picker.CanCreate);
    }

    /// <summary>
    /// Except the suggestion this photograph is in already.
    /// </summary>
    /// <remarks>
    /// It carries the tick saying where the picture is now, and the way out of
    /// it. Dropped with the rest, a photograph filed into a suggestion would
    /// show as belonging nowhere and could not be taken out from here at all.
    /// </remarks>
    [Fact]
    public void TheSuggestionAPhotographIsAlreadyInIsStillShown()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask, () => Task.CompletedTask);

        AlbumSummary inIt = Proposed("Tampines Estate", Day);

        picker.Open(
            [inIt, Proposed("Elsewhere", Day), Made("Zoo 2026", Day)],
            inIt.Id,
            "Put this photograph in an album",
            "hint");

        Assert.Equal(
            ["Tampines Estate", "Zoo 2026"],
            picker.Choices.Select(choice => choice.Name).Order());

        Assert.True(picker.Choices.Single(choice => choice.Name == "Tampines Estate").IsCurrent);

        // And the way out of it is offered, and named.
        Assert.True(picker.IsInOne);
        Assert.Equal("Take it out of Tampines Estate", picker.TakeOutLabel);
    }

    /// <summary>
    /// The untouched list is the recent few, under one heading.
    /// </summary>
    /// <remarks>
    /// It used to be every album you owned, banded into the top four and an
    /// "everything else" over the rest. With the app's own suggestions gone
    /// there is no long tail worth scrolling, and the second band said nothing
    /// over three more rows.
    /// </remarks>
    [Fact]
    public void TheListIsTheRecentFewUnderOneHeading()
    {
        AlbumPicker picker = WithMany();

        Assert.Equal("RECENT", picker.Choices[0].Heading);
        Assert.All(picker.Choices.Skip(1), choice => Assert.False(choice.HasHeading));
    }

    /// <summary>
    /// A longer list stops, and says how many it stopped short of.
    /// </summary>
    /// <remarks>
    /// Said rather than left to be discovered: a list that quietly stops is a
    /// list somebody concludes their album has gone from.
    /// </remarks>
    [Fact]
    public void ALongerListSaysHowManyItIsNotShowing()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [.. Enumerable.Range(0, 11).Select(at => Made($"Album {at}", Day.AddDays(at)))],
            0,
            "Put this photograph in an album",
            "hint");

        Assert.Equal(8, picker.Choices.Count);
        Assert.True(picker.HasMore);
        Assert.Equal("3 more albums. Type to find one.", picker.MoreCaption);

        // The newest first, so the three left out are the three oldest.
        Assert.Equal("Album 10", picker.Choices[0].Name);
        Assert.DoesNotContain(picker.Choices, choice => choice.Name == "Album 0");
    }

    /// <summary>
    /// And typing reaches the ones the list stopped short of.
    /// </summary>
    /// <remarks>
    /// The list is shortened, not the library. Searching only what was on
    /// screen would put an older album out of reach altogether, which is a
    /// worse thing than a long list.
    /// </remarks>
    [Fact]
    public void TypingReachesTheAlbumsTheListDidNotShow()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [.. Enumerable.Range(0, 11).Select(at => Made($"Album {at}", Day.AddDays(at)))],
            0,
            "Put this photograph in an album",
            "hint");

        Assert.DoesNotContain(picker.Choices, choice => choice.Name == "Album 0");

        picker.Typed = "Album 0";

        Assert.Equal(["Album 0"], picker.Choices.Select(choice => choice.Name));

        // And nothing is being held back any more, so nothing is said about it.
        Assert.False(picker.HasMore);
    }

    /// <summary>
    /// Typing takes the headings away.
    /// </summary>
    /// <remarks>
    /// On a narrowed list they would band three rows into two, and "recent" is
    /// not what somebody who has typed four letters is looking at the list for.
    /// </remarks>
    [Fact]
    public void TypingTakesTheHeadingsAway()
    {
        AlbumPicker picker = WithMany();

        picker.Typed = "a";

        Assert.NotEmpty(picker.Choices);
        Assert.All(picker.Choices, choice => Assert.False(choice.HasHeading));
    }

    /// <summary>
    /// A library with nothing but proposals in it offers nothing to choose.
    /// </summary>
    /// <remarks>
    /// Which is the state a library is in before anybody has made an album. The
    /// list is empty rather than three hundred suggestions long, and the way on
    /// is to name one.
    /// </remarks>
    [Fact]
    public void WithNoAlbumsOfYourOwn_ThereIsNothingToChooseFrom()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [Proposed("One", Day), Proposed("Two", Day), Proposed("Three", Day)],
            0,
            "Put these 5 photographs in an album",
            "hint");

        Assert.Empty(picker.Choices);

        picker.Typed = "Holiday";
        Assert.True(picker.CanCreate);
    }

    /// <summary>A list holding everything says nothing about more.</summary>
    [Fact]
    public void AListShowingEverythingSaysNothingAboutMore()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [Made("One", Day), Made("Two", Day)], 0, "Put this photograph in an album", "hint");

        Assert.Equal(2, picker.Choices.Count);
        Assert.False(picker.HasMore);
    }

    /// <summary>
    /// Recent means made, renamed or filed into - whichever happened last.
    /// </summary>
    /// <remarks>
    /// Both halves, and it takes both. Filing alone leaves an album just created
    /// sitting at the bottom with nothing in it, which is the one somebody is
    /// most likely to have made in order to file into. Creation alone forgets
    /// that a holiday from 2019 added to last night is the one in hand.
    /// </remarks>
    [Theory]
    [InlineData(null, null, null)]
    [InlineData(5, null, 5)]
    [InlineData(null, 5, 5)]
    [InlineData(2, 9, 9)]
    [InlineData(9, 2, 9)]
    public void RecentMeansMadeOrFiledIntoWhicheverIsLater(
        int? added, int? named, int? expected)
    {
        var album = new AlbumSummary(
            1, "Sunday", Day, Day, AlbumKind.Trip, AlbumOrigin.Made, 3,
            CoverThumbnailName: null,
            CollectionId: null,
            LastAddedUtc: added is int a ? Day.AddDays(a) : null,
            NamedUtc: named is int n ? Day.AddDays(n) : null);

        Assert.Equal(expected is int e ? Day.AddDays(e) : null, album.LastUsedUtc);
    }

    /// <summary>
    /// An album made a moment ago and holding nothing is the most recent of all.
    /// </summary>
    /// <remarks>
    /// The case the first version of this got wrong: with recency read off the
    /// memberships alone, an empty album had no moment at all and sorted below
    /// every album that did.
    /// </remarks>
    [Fact]
    public void AnAlbumJustMadeAndStillEmptyComesFirst()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [
                new AlbumSummary(
                    1, "Filed into last week", Day, Day, AlbumKind.Trip, AlbumOrigin.Made, 40,
                    null, null, LastAddedUtc: Day.AddDays(3), NamedUtc: Day),
                new AlbumSummary(
                    2, "Made just now", Day, Day, AlbumKind.Trip, AlbumOrigin.Made, 0,
                    null, null, LastAddedUtc: null, NamedUtc: Day.AddDays(9)),
            ],
            0,
            "Put this photograph in an album",
            "hint");

        Assert.Equal(
            ["Made just now", "Filed into last week"],
            picker.Choices.Select(choice => choice.Name));
    }

    /// <summary>
    /// Create promises what it will actually do with the selection.
    /// </summary>
    /// <remarks>
    /// It said it would put "the photograph" in it, whatever number had been
    /// ticked - the one line in the picker that had never learnt there was a
    /// second way in.
    /// </remarks>
    [Fact]
    public void CreateSaysHowManyItWouldPutInTheNewAlbum()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open([], 0, "Put this photograph in an album", "hint");
        Assert.Contains("the photograph", picker.CreateTip, StringComparison.Ordinal);

        picker.Open(
            [], 0, "prompt", "hint",
            "Create an album with this name and put the 12 ticked photographs in it");

        Assert.Contains("12 ticked photographs", picker.CreateTip, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creating is offered when the search does not find the album you wanted.
    /// </summary>
    /// <remarks>
    /// The box is a search box now. The offer to create used to be a button
    /// standing beside it whatever was typed, which is what made the box read as
    /// the way to name a new album rather than the way to find an old one - and
    /// a picker that has always filtered was reported as having no search.
    /// </remarks>
    [Fact]
    public void CreatingIsOfferedWhenTheSearchFindsNothing()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open([Made("Sentosa", Day)], 0, "prompt", "hint");

        // Nothing typed: nothing to name, so nothing on offer.
        Assert.False(picker.CanCreate);

        picker.Typed = "Bali";

        Assert.True(picker.CanCreate);
        Assert.Empty(picker.Choices);
        Assert.Equal("Create “Bali”", picker.CreateLabel);
    }

    /// <summary>
    /// And a name that is already an album's has nothing left to create.
    /// </summary>
    [Fact]
    public void AnAlbumThatAlreadyExistsIsNotOfferedAgain()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open([Made("Sentosa", Day)], 0, "prompt", "hint");

        picker.Typed = "sentosa";

        Assert.False(picker.CanCreate);
        Assert.False(picker.CreateCommand.CanExecute(null));
    }

    /// <summary>
    /// A name inside another album's name can still be created.
    /// </summary>
    /// <remarks>
    /// The distinction the offer turns on, and the reason it is not simply
    /// "nothing matched". The box narrows on any part of a name, so typing
    /// "Bali" beside an album called "Bali 2019" finds something - and an offer
    /// that went away whenever anything matched would make the name "Bali"
    /// impossible to give at all.
    /// </remarks>
    [Fact]
    public void ANameInsideAnotherAlbumsNameCanStillBeCreated()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open([Made("Bali 2019", Day)], 0, "prompt", "hint");

        picker.Typed = "Bali";

        Assert.Single(picker.Choices);
        Assert.True(picker.CanCreate);
    }

    /// <summary>
    /// The window asks before a selection moves, and the view model no longer
    /// does it quietly.
    /// </summary>
    /// <remarks>
    /// Read as text, because a modal cannot be raised from a view-model suite
    /// and the question is the whole feature. It has to be a handler rather than
    /// a command for the reason every delete in this app is: the confirmation
    /// pumps messages, and CanExecute cannot be trusted across it.
    ///
    /// <para>Filing a selection was the one thing on this wall that happened the
    /// instant it was asked for - and Select all followed by one click on a list
    /// is the same gesture whether it moves five photographs or twenty
    /// thousand.</para>
    /// </remarks>
    [Fact]
    public void TheWindowAsksBeforeASelectionMoves()
    {
        string shell = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));
        string model = File.ReadAllText(AppMarkup.PathTo("ViewModels", "MainViewModel.cs"));

        int handler = shell.IndexOf(
            "private async void OnPlacingChosen", StringComparison.Ordinal);
        Assert.True(handler > 0, "the window no longer answers the wall");

        int confirm = shell.IndexOf("AppDialog.Confirm", handler, StringComparison.Ordinal);
        int overlay = shell.IndexOf("UnderOverlayAsync", handler, StringComparison.Ordinal);

        Assert.True(confirm > handler, "nothing is asked before the photographs move");
        Assert.True(overlay > confirm, "the move is started before the question is answered");

        // And the way past it is gone: a second subscriber would move them
        // without asking.
        Assert.DoesNotContain("PlacingChosen += OnPlacingChosen", model, StringComparison.Ordinal);
    }

    /// <summary>
    /// A selection is answered beside the bar; one photograph is answered in the
    /// middle.
    /// </summary>
    /// <remarks>
    /// The photographs being filed are ticked on the wall directly behind the
    /// bar, and a modal shade would dim the one thing worth looking at while
    /// answering a question about it. In the viewer the picture is already the
    /// whole screen and there is nothing behind a panel worth seeing.
    /// </remarks>
    [Fact]
    public void ASelectionIsAnsweredBesideTheBarAndOnePhotographInTheMiddle()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open([Made("Sentosa", Day)], 0, "Put this photograph in an album", "hint");

        Assert.True(picker.IsOpenInTheMiddle);
        Assert.False(picker.IsOpenBesideTheBar);

        picker.Open(
            [Made("Sentosa", Day)],
            0,
            "Put these 5 photographs in an album",
            "hint",
            makeTip: null,
            anchored: true);

        Assert.True(picker.IsOpenBesideTheBar);
        Assert.False(picker.IsOpenInTheMiddle);
    }

    /// <summary>And neither is open once it is closed, however it closed.</summary>
    /// <remarks>
    /// A popover dismisses itself when the pointer goes elsewhere, and it does
    /// that by writing IsOpen rather than by calling Close. Everything that has
    /// to happen when the list goes away therefore hangs off the property: left
    /// in Close, a panel clicked away would keep what was being asked about
    /// spoken for and leave last time's typing in the box.
    /// </remarks>
    [Fact]
    public void ClickingAPopoverAwayForgetsWhatItWasAsking()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [Made("Sentosa", Day)],
            0,
            "Put these 5 photographs in an album",
            "hint",
            makeTip: null,
            anchored: true);

        picker.Typed = "Sen";
        Assert.NotEmpty(picker.Choices);

        // What the popover itself does when the pointer goes elsewhere.
        picker.IsOpen = false;

        Assert.False(picker.IsOpenBesideTheBar);
        Assert.False(picker.IsOpenInTheMiddle);
        Assert.Equal(string.Empty, picker.Typed);
        Assert.Empty(picker.Choices);
    }

    private static AlbumPicker WithMany()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [
                Made("A", Day.AddDays(9)),
                Made("B", Day.AddDays(8)),
                Made("C", Day.AddDays(7)),
                Made("D", Day.AddDays(6)),
                Made("E", Day.AddDays(5)),
                Proposed("F", Day.AddDays(20)),
            ],
            0,
            "Put these 5 photographs in an album",
            "hint");

        return picker;
    }

    private static AlbumSummary Made(string name, DateTime? lastAdded) =>
        Album(name, AlbumOrigin.Made, lastAdded);

    private static AlbumSummary Proposed(string name, DateTime? lastAdded) =>
        Album(name, AlbumOrigin.Proposed, lastAdded);

    private static AlbumSummary Album(string name, AlbumOrigin origin, DateTime? lastAdded) =>
        new(
            name.GetHashCode(StringComparison.Ordinal),
            name,
            Day,
            Day,
            AlbumKind.Trip,
            origin,
            3,
            CoverThumbnailName: null,
            CollectionId: null,
            LastAddedUtc: lastAdded);

    private static AlbumSummary Single(IReadOnlyList<AlbumSummary> albums, string name) =>
        albums.Single(album => album.Name == name);

    private IAlbumRepository Repository() => new SqliteAlbumRepository(_db);

    private int Add(string relativePath)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024,
            ModifiedUtc = Day,
            CreatedUtc = Day,
            IndexedUtc = Day,
            TakenUtc = Day,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = relativePath,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return asset.Id;
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
            // A temporary folder; the handle is the database's own.
        }
    }
}
