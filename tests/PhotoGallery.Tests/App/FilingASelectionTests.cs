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

    /// <summary>And one holding nothing says nothing, rather than a date of zero.</summary>
    [Fact]
    public async Task AnEmptyAlbumHasNoSuchMoment()
    {
        await Repository().CreateAsync("Empty");

        Assert.Null(Single(await Repository().GetAsync(), "Empty").LastAddedUtc);
    }

    /// <summary>
    /// The picker puts your own albums first, most recently filed into at the
    /// top.
    /// </summary>
    /// <remarks>
    /// Recency is measured over albums you made or kept and only those, and that
    /// is the point rather than a detail. A rebuild writes every proposal's
    /// memberships in one go: on the first real library 284 of 291 albums share
    /// a last-added moment to the millisecond, so ordering everything by it
    /// would hoist whatever the last scan happened to touch and call it recent.
    /// </remarks>
    [Fact]
    public void ThePickerPutsYourOwnAlbumsFirstAndTheLatestOfThemOnTop()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        // The scan's moment is later than either of the two the user filed into,
        // so ordering by the date alone would put both proposals above them.
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
            ["Our Books", "Sentosa", "Never used", "Nine days with Shirley", "Fourteen days with Jia Le"],
            picker.Choices.Select(choice => choice.Name));
    }

    /// <summary>
    /// The top few carry a heading, and so does what is under them.
    /// </summary>
    [Fact]
    public void TheTopFewAreBandedAsRecent()
    {
        AlbumPicker picker = WithMany();

        Assert.Equal("RECENT", picker.Choices[0].Heading);
        Assert.Equal("EVERYTHING ELSE", picker.Choices[4].Heading);
        Assert.All(
            picker.Choices.Where((_, at) => at is not (0 or 4)),
            choice => Assert.False(choice.HasHeading));
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
    /// A library with nothing but proposals in it gets no headings.
    /// </summary>
    /// <remarks>
    /// Which is the state a library is in before anybody has made an album. A
    /// RECENT band over four albums nobody has ever used is a label that is
    /// simply untrue.
    /// </remarks>
    [Fact]
    public void WithNoAlbumsOfYourOwn_ThereAreNoHeadings()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [Proposed("One", Day), Proposed("Two", Day), Proposed("Three", Day)],
            0,
            "Put these 5 photographs in an album",
            "hint");

        Assert.All(picker.Choices, choice => Assert.False(choice.HasHeading));
    }

    /// <summary>And a short list of only your own gets none either.</summary>
    /// <remarks>
    /// There is nothing under the band for the heading to distinguish it from.
    /// </remarks>
    [Fact]
    public void WithNothingUnderTheBand_ThereAreNoHeadings()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open(
            [Made("One", Day), Made("Two", Day)], 0, "Put this photograph in an album", "hint");

        Assert.All(picker.Choices, choice => Assert.False(choice.HasHeading));
    }

    /// <summary>
    /// Make promises what it will actually do with the selection.
    /// </summary>
    /// <remarks>
    /// It said it would put "the photograph" in it, whatever number had been
    /// ticked - the one line in the picker that had never learnt there was a
    /// second way in.
    /// </remarks>
    [Fact]
    public void MakeSaysHowManyItWouldPutInTheNewAlbum()
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask);

        picker.Open([], 0, "Put this photograph in an album", "hint");
        Assert.Contains("the photograph", picker.MakeTip, StringComparison.Ordinal);

        picker.Open([], 0, "prompt", "hint", "Make an album with this name and put the 12 ticked photographs in it");
        Assert.Contains("12 ticked photographs", picker.MakeTip, StringComparison.Ordinal);
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
