using Microsoft.EntityFrameworkCore;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// What keeping and dismissing a suggestion write down here, against a real
/// SQLite file with the migrations applied.
/// </summary>
/// <remarks>
/// Keeping and dismissing are two answers about the same run of days, and when
/// two laptops give different ones the later one stands. That is only possible
/// if each of them says when it was given - which neither used to - so these
/// are claims about the moment each write records, on every path that keeps or
/// dismisses.
///
/// <para>Working on a kept suggestion counts as keeping it again. Renaming it,
/// putting it on a shelf, choosing its cover or adding photographs to it is a
/// later act of wanting the album, so a dismissal made on another laptop before
/// that work no longer takes it away. An album somebody made was never a
/// suggestion and is never dated as a keep.</para>
/// </remarks>
public sealed class KeepingASuggestionTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly SqliteAlbumRepository _albums;
    private readonly SqliteCollectionRepository _shelves;
    private int _nextPhoto;

    public KeepingASuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-keeping-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();

        _albums = new SqliteAlbumRepository(_db);
        _shelves = new SqliteCollectionRepository(_db);
    }

    /// <summary>
    /// Every way of keeping a suggestion says when it was kept.
    /// </summary>
    /// <remarks>
    /// Four ways in: the Keep button, ticking it onto a shelf, shelving it from
    /// its own panel, and the rebuild promoting a suggestion an older release
    /// shelved without keeping. Shelving is keeping in one write, so the keep
    /// and the shelving carry one moment; the old shelving was the keep, so
    /// its date is the keep's.
    /// </remarks>
    [Fact]
    public async Task KeepingASuggestionSaysWhen()
    {
        int accepted = Suggestion("March 2019", "2019-03-03..2019-03-05");
        int ticked = Suggestion("April 2019", "2019-04-03..2019-04-05");
        int panel = Suggestion("May 2019", "2019-05-03..2019-05-05");
        int legacy = Suggestion("June 2019", "2019-06-03..2019-06-05");

        int holiday = await _shelves.CreateAsync("Holiday");
        int weekends = await _shelves.CreateAsync("Weekends");
        int older = await _shelves.CreateAsync("From before");
        ShelvedTheOldWay(legacy, older, Monday);

        DateTime before = DateTime.UtcNow;

        await _albums.AcceptAsync(accepted);
        await _shelves.SetAlbumsAsync(holiday, [ticked]);
        await _shelves.SetAlbumCollectionAsync(panel, weekends);
        await _albums.SaveProposalsAsync([]);
        _db.ChangeTracker.Clear();

        Album keep = Row(accepted);
        Assert.Equal(AlbumOrigin.Accepted, keep.Origin);
        Assert.NotNull(keep.KeptUtc);
        Assert.InRange(keep.KeptUtc.Value, before, DateTime.UtcNow);

        Album tick = Row(ticked);
        Assert.NotNull(tick.KeptUtc);
        Assert.Equal(tick.ShelvedUtc, tick.KeptUtc);

        Album shelved = Row(panel);
        Assert.NotNull(shelved.KeptUtc);
        Assert.Equal(shelved.ShelvedUtc, shelved.KeptUtc);

        Album promoted = Row(legacy);
        Assert.Equal(AlbumOrigin.Accepted, promoted.Origin);
        Assert.Equal(Monday, promoted.KeptUtc);
    }

    /// <summary>
    /// Dismissing a suggestion remembers when, under its run of days, and
    /// still takes the row away.
    /// </summary>
    /// <remarks>
    /// The refusals stay what they were, one per photograph, and carry the same
    /// moment as the dismissal - one press is one decision. The row goes
    /// outright rather than leaving a tombstone, so those days can still be
    /// offered afresh from photographs nobody refused, as they always could. A
    /// second dismissal of those days, offered again, is the later answer and
    /// replaces the first.
    /// </remarks>
    [Fact]
    public async Task DismissingASuggestionRemembersWhen()
    {
        const string days = "2019-03-03..2019-03-05";

        int first = Suggestion("March 2019", days);
        Photo("a.jpg", first);
        Photo("b.jpg", first);

        await _albums.DismissAsync(first);
        _db.ChangeTracker.Clear();

        AlbumDismissal dismissal = Assert.Single(_db.AlbumDismissals.AsNoTracking());
        Assert.Equal(days, dismissal.ProposalKey);
        Assert.Equal(Guid.Empty, dismissal.DismissedBy);

        List<AlbumRejection> refused = [.. _db.AlbumRejections.AsNoTracking()];
        Assert.Equal(2, refused.Count);
        Assert.All(refused, rejection => Assert.Equal(dismissal.DismissedUtc, rejection.RejectedUtc));

        Assert.Empty(_db.Albums.IgnoreQueryFilters());

        // Backdated, so the second press is plainly the later one whatever the
        // clock's resolution.
        AlbumDismissal stored = _db.AlbumDismissals.Single();
        stored.DismissedUtc = Monday;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        int again = Suggestion("March 2019", days);
        int c = Photo("c.jpg", again);

        await _albums.DismissAsync(again);
        _db.ChangeTracker.Clear();

        AlbumDismissal later = Assert.Single(_db.AlbumDismissals.AsNoTracking());
        Assert.Equal(
            _db.AlbumRejections.AsNoTracking().Single(rejection => rejection.AssetId == c).RejectedUtc,
            later.DismissedUtc);
        Assert.True(later.DismissedUtc > Monday);
    }

    /// <summary>Renaming a kept suggestion keeps it again.</summary>
    [Fact]
    public async Task RenamingAKeptSuggestionKeepsItAgain()
    {
        int kept = KeptSuggestion("March 2019", "2019-03-03..2019-03-05", Monday);

        await _albums.RenameAsync(kept, "Genting");
        _db.ChangeTracker.Clear();

        Album row = Row(kept);
        Assert.NotNull(row.NamedUtc);
        Assert.Equal(row.NamedUtc, row.KeptUtc);
    }

    /// <summary>Choosing the cover of a kept suggestion keeps it again.</summary>
    [Fact]
    public async Task ChoosingTheCoverOfAKeptSuggestionKeepsItAgain()
    {
        int kept = KeptSuggestion("March 2019", "2019-03-03..2019-03-05", Monday);
        int photo = Photo("a.jpg", kept);

        Assert.True(await _albums.SetCoverAsync(kept, photo));
        _db.ChangeTracker.Clear();

        Album row = Row(kept);
        Assert.NotNull(row.CoverChosenUtc);
        Assert.Equal(row.CoverChosenUtc, row.KeptUtc);
    }

    /// <summary>Putting a kept suggestion on a shelf keeps it again.</summary>
    [Fact]
    public async Task ShelvingAKeptSuggestionKeepsItAgain()
    {
        int kept = KeptSuggestion("March 2019", "2019-03-03..2019-03-05", Monday);
        int holiday = await _shelves.CreateAsync("Holiday");

        await _shelves.SetAlbumCollectionAsync(kept, holiday);
        _db.ChangeTracker.Clear();

        Album row = Row(kept);
        Assert.NotNull(row.ShelvedUtc);
        Assert.Equal(row.ShelvedUtc, row.KeptUtc);
    }

    /// <summary>
    /// Ticking a kept suggestion onto a shelf keeps it again, as shelving it
    /// from its own panel does.
    /// </summary>
    /// <remarks>
    /// The tick list is the other way on to a shelf. Answering differently
    /// depending on which of the two somebody happened to use would make the
    /// same decision survive a dismissal on one screen and not on the other.
    /// </remarks>
    [Fact]
    public async Task TickingAKeptSuggestionOntoAShelfKeepsItAgain()
    {
        int kept = KeptSuggestion("March 2019", "2019-03-03..2019-03-05", Monday);
        int holiday = await _shelves.CreateAsync("Holiday");

        await _shelves.SetAlbumsAsync(holiday, [kept]);
        _db.ChangeTracker.Clear();

        Album row = Row(kept);
        Assert.NotNull(row.ShelvedUtc);
        Assert.Equal(row.ShelvedUtc, row.KeptUtc);
    }

    /// <summary>Adding photographs to a kept suggestion keeps it again.</summary>
    [Fact]
    public async Task AddingPhotographsToAKeptSuggestionKeepsItAgain()
    {
        int kept = KeptSuggestion("March 2019", "2019-03-03..2019-03-05", Monday);
        int photo = Photo("a.jpg", albumId: null);

        await _albums.AddAsync(kept, [photo]);
        _db.ChangeTracker.Clear();

        AlbumMember added = _db.AlbumMembers.AsNoTracking().Single(member => member.AssetId == photo);
        Assert.Equal(added.AddedUtc, Row(kept).KeptUtc);
    }

    /// <summary>
    /// An album somebody made is never dated as a keep, whatever is done to it.
    /// </summary>
    /// <remarks>
    /// It was never a suggestion, so there is no dismissal it could lose to
    /// and no keep to renew. A date here would be a keep nobody made.
    /// </remarks>
    [Fact]
    public async Task WorkingOnAnAlbumSomebodyMadeNeverDatesAKeep()
    {
        int made = Made("Best shots");
        int photo = Photo("a.jpg", albumId: null);
        int holiday = await _shelves.CreateAsync("Holiday");
        int weekends = await _shelves.CreateAsync("Weekends");

        await _albums.AddAsync(made, [photo]);
        await _albums.RenameAsync(made, "Best of all");
        Assert.True(await _albums.SetCoverAsync(made, photo));
        await _shelves.SetAlbumCollectionAsync(made, holiday);
        await _shelves.SetAlbumsAsync(weekends, [made]);
        _db.ChangeTracker.Clear();

        Album row = Row(made);
        Assert.Equal(AlbumOrigin.Made, row.Origin);
        Assert.Null(row.KeptUtc);
    }

    private Album Row(int albumId) =>
        _db.Albums.IgnoreQueryFilters().AsNoTracking().Single(album => album.Id == albumId);

    private int Suggestion(string name, string proposalKey) =>
        Add(name, proposalKey, AlbumOrigin.Proposed, keptUtc: null);

    private int KeptSuggestion(string name, string proposalKey, DateTime keptUtc) =>
        Add(name, proposalKey, AlbumOrigin.Accepted, keptUtc);

    private int Made(string name) => Add(name, proposalKey: null, AlbumOrigin.Made, keptUtc: null);

    private int Add(string name, string? proposalKey, AlbumOrigin origin, DateTime? keptUtc)
    {
        var album = new Album
        {
            Name = name,
            StartUtc = new DateTime(2019, 3, 3, 12, 0, 0, DateTimeKind.Unspecified),
            EndUtc = new DateTime(2019, 3, 5, 18, 0, 0, DateTimeKind.Unspecified),
            Kind = AlbumKind.Event,
            Origin = origin,
            ProposalKey = proposalKey,
            KeptUtc = keptUtc,
            BuiltUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        _db.Albums.Add(album);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return album.Id;
    }

    /// <summary>
    /// A suggestion shelved without being kept, written straight to the
    /// columns - which is the state a library filled by an older release holds.
    /// </summary>
    private void ShelvedTheOldWay(int albumId, int collectionId, DateTime shelvedUtc)
    {
        Album album = _db.Albums.Single(row => row.Id == albumId);
        album.CollectionId = collectionId;
        album.ShelvedUtc = shelvedUtc;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private int Photo(string relativePath, int? albumId)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024 + _nextPhoto++,
            ModifiedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IndexedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = relativePath,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();

        if (albumId is int album)
        {
            _db.AlbumMembers.Add(new AlbumMember { AssetId = asset.Id, AlbumId = album });
            _db.SaveChanges();
        }

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
            // A temporary folder left behind is not a failed test.
        }
    }
}
