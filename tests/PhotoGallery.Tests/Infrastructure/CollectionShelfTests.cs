using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// What a shelf of albums does, against a real SQLite file with the migrations
/// applied.
/// </summary>
/// <remarks>
/// Two of these are claims about the database rather than about a handler - an
/// album is on one collection because it is one column, and two collections
/// cannot share a name because of a filtered unique index. Neither would be
/// proved by an in-memory model.
/// </remarks>
public sealed class CollectionShelfTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly SqliteCollectionRepository _shelves;

    public CollectionShelfTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-shelves-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();

        _shelves = new SqliteCollectionRepository(_db);
    }

    [Fact]
    public async Task AShelfSaysHowManyAlbumsAndHowManyPhotographsAreOnIt()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        int genting = Album("Genting");
        int bali = Album("Bali");
        Photo("a.jpg", genting);
        Photo("b.jpg", genting);
        Photo("c.jpg", bali);

        await _shelves.SetAlbumsAsync(holiday, [genting, bali]);
        _db.ChangeTracker.Clear();

        CollectionSummary shelf = Assert.Single(await _shelves.GetAsync());
        Assert.Equal("Holiday", shelf.Name);
        Assert.Equal(2, shelf.AlbumCount);
        Assert.Equal(3, shelf.PhotoCount);
    }

    /// <summary>
    /// The one-shelf rule, which is the whole reason it is a column.
    /// </summary>
    [Fact]
    public async Task AnAlbumPutOnASecondShelfComesOffTheFirst()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        int weekends = await _shelves.CreateAsync("Weekends");
        int genting = Album("Genting");

        await _shelves.SetAlbumsAsync(holiday, [genting]);
        await _shelves.SetAlbumsAsync(weekends, [genting]);
        _db.ChangeTracker.Clear();

        Assert.Equal(weekends, await ShelfOf(genting));

        IReadOnlyList<CollectionSummary> all = await _shelves.GetAsync();
        Assert.Equal(0, all.Single(shelf => shelf.Id == holiday).AlbumCount);
        Assert.Equal(1, all.Single(shelf => shelf.Id == weekends).AlbumCount);
    }

    /// <summary>
    /// And it says which shelf it came off, because a rule the user did not ask
    /// about must not be enforced in silence.
    /// </summary>
    [Fact]
    public async Task MovingAnAlbumSaysWhichShelfItCameOff()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        int weekends = await _shelves.CreateAsync("Weekends");
        int genting = Album("Genting");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        _db.ChangeTracker.Clear();

        CollectionFillResult result = await _shelves.SetAlbumsAsync(weekends, [genting]);

        Assert.Equal(1, result.Added);
        Assert.Equal("Holiday", Assert.Single(result.From));
    }

    [Fact]
    public async Task AnAlbumThatWasOnNoShelfNamesNothingItLeft()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        int genting = Album("Genting");

        CollectionFillResult result = await _shelves.SetAlbumsAsync(holiday, [genting]);

        Assert.Equal(1, result.Added);
        Assert.Empty(result.From);
    }

    /// <summary>
    /// The tick list is the shelf, so a cleared tick is how an album leaves.
    /// </summary>
    [Fact]
    public async Task SavingWithoutAnAlbumTakesItOff()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        int genting = Album("Genting");
        int bali = Album("Bali");

        await _shelves.SetAlbumsAsync(holiday, [genting, bali]);
        _db.ChangeTracker.Clear();

        CollectionFillResult result = await _shelves.SetAlbumsAsync(holiday, [bali]);
        _db.ChangeTracker.Clear();

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Removed);
        Assert.Null(await ShelfOf(genting));
        Assert.Equal(holiday, await ShelfOf(bali));
    }

    /// <summary>
    /// Putting a suggestion on a shelf is deciding to keep it, so it is kept in
    /// the same write rather than left in the queue of questions.
    /// </summary>
    [Fact]
    public async Task TickingASuggestionKeepsIt()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        int proposed = Album("March 2019", "2019-03-20..2019-03-20");

        CollectionFillResult result = await _shelves.SetAlbumsAsync(holiday, [proposed]);
        _db.ChangeTracker.Clear();

        Assert.Equal(1, result.Kept);
        Assert.Equal(
            AlbumOrigin.Accepted,
            await _db.Albums.Where(a => a.Id == proposed).Select(a => a.Origin).SingleAsync());
    }

    [Fact]
    public async Task KeepingAnAlbumThatWasAlreadyTheUsersIsNotCountedAsKept()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        int genting = Album("Genting");

        CollectionFillResult result = await _shelves.SetAlbumsAsync(holiday, [genting]);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Kept);
    }

    /// <summary>
    /// Nothing on a shelf is destroyed by taking the shelf away - the same rule
    /// removing an album follows for its photographs.
    /// </summary>
    [Fact]
    public async Task RemovingAShelfLeavesItsAlbumsOnNone()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        int genting = Album("Genting");
        await _shelves.SetAlbumsAsync(holiday, [genting]);
        _db.ChangeTracker.Clear();

        await _shelves.DeleteAsync(holiday);
        _db.ChangeTracker.Clear();

        Assert.Empty(await _shelves.GetAsync());
        Assert.True(await _db.Albums.AnyAsync(a => a.Id == genting));
        Assert.Null(await ShelfOf(genting));
    }

    /// <summary>
    /// A tombstone, so a merge from a machine that still holds it cannot put it
    /// back, and it is out of every query in the app meanwhile.
    /// </summary>
    [Fact]
    public async Task ARemovedShelfLeavesATombstone()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.DeleteAsync(holiday);
        _db.ChangeTracker.Clear();

        Assert.Empty(await _db.Collections.ToListAsync());
        Assert.NotNull(
            await _db.Collections.IgnoreQueryFilters().SingleAsync(c => c.Id == holiday));
    }

    [Fact]
    public async Task TwoShelvesCannotShareAName()
    {
        await _shelves.CreateAsync("Holiday");

        await Assert.ThrowsAsync<DbUpdateException>(() => _shelves.CreateAsync("Holiday"));
    }

    /// <summary>
    /// A name given back by a removal is free to use again: a tombstone records
    /// what happened rather than reserving a word.
    /// </summary>
    [Fact]
    public async Task ANameComesBackWhenTheShelfIsRemoved()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        await _shelves.DeleteAsync(holiday);
        _db.ChangeTracker.Clear();

        int again = await _shelves.CreateAsync("Holiday");

        Assert.NotEqual(holiday, again);
        Assert.Equal("Holiday", Assert.Single(await _shelves.GetAsync()).Name);
    }

    /// <summary>
    /// The band is read by name, because a theme has no place on a calendar and
    /// somebody scanning it is looking for a word.
    /// </summary>
    [Fact]
    public async Task TheBandIsInNameOrder()
    {
        await _shelves.CreateAsync("Weekends");
        await _shelves.CreateAsync("Holiday");
        await _shelves.CreateAsync("Adam");

        Assert.Equal(
            ["Adam", "Holiday", "Weekends"],
            (await _shelves.GetAsync()).Select(shelf => shelf.Name));
    }

    /// <summary>
    /// The mosaic reads newest album first, so a shelf that has just been added
    /// to shows what was added.
    /// </summary>
    [Fact]
    public async Task TheMosaicIsMostRecentAlbumFirst()
    {
        int holiday = await _shelves.CreateAsync("Holiday");

        int older = Album("Genting", ends: new DateTime(2019, 3, 5, 0, 0, 0, DateTimeKind.Unspecified));
        int newer = Album("Bali", ends: new DateTime(2023, 7, 1, 0, 0, 0, DateTimeKind.Unspecified));
        Cover(older, Photo("old.jpg", older));
        Cover(newer, Photo("new.jpg", newer));

        await _shelves.SetAlbumsAsync(holiday, [older, newer]);
        _db.ChangeTracker.Clear();

        Assert.Equal(
            ["new.jpg", "old.jpg"],
            Assert.Single(await _shelves.GetAsync()).CoverThumbnailNames);
    }

    /// <summary>Four tiles is what the mosaic has, however many albums are on it.</summary>
    [Fact]
    public async Task TheMosaicStopsAtFourCovers()
    {
        int holiday = await _shelves.CreateAsync("Holiday");
        List<int> albums = [];

        for (int day = 1; day <= 6; day++)
        {
            int album = Album(
                $"Trip {day}",
                ends: new DateTime(2023, 7, day, 0, 0, 0, DateTimeKind.Unspecified));
            Cover(album, Photo($"cover{day}.jpg", album));
            albums.Add(album);
        }

        await _shelves.SetAlbumsAsync(holiday, albums);
        _db.ChangeTracker.Clear();

        CollectionSummary shelf = Assert.Single(await _shelves.GetAsync());
        Assert.Equal(6, shelf.AlbumCount);
        Assert.Equal(
            ["cover6.jpg", "cover5.jpg", "cover4.jpg", "cover3.jpg"],
            shelf.CoverThumbnailNames);
    }

    [Fact]
    public async Task AShelfWithNothingOnItStillShows()
    {
        await _shelves.CreateAsync("Holiday");

        CollectionSummary shelf = Assert.Single(await _shelves.GetAsync());
        Assert.Equal(0, shelf.AlbumCount);
        Assert.Empty(shelf.CoverThumbnailNames);
    }

    [Fact]
    public async Task FillingAShelfThatIsNotThereDoesNothing()
    {
        int genting = Album("Genting");

        CollectionFillResult result = await _shelves.SetAlbumsAsync(404, [genting]);

        Assert.Equal(CollectionFillResult.Nothing, result);
        Assert.Null(await ShelfOf(genting));
    }

    private async Task<int?> ShelfOf(int albumId) =>
        await _db.Albums.Where(a => a.Id == albumId).Select(a => a.CollectionId).SingleAsync();

    private int Album(string name, string? proposalKey = null, DateTime? ends = null)
    {
        var album = new Album
        {
            Name = name,
            StartUtc = new DateTime(2019, 3, 3, 12, 0, 0, DateTimeKind.Unspecified),
            EndUtc = ends ?? new DateTime(2019, 3, 5, 18, 0, 0, DateTimeKind.Unspecified),
            Kind = AlbumKind.Event,
            Origin = proposalKey is null ? AlbumOrigin.Made : AlbumOrigin.Proposed,
            ProposalKey = proposalKey,
            BuiltUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        _db.Albums.Add(album);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return album.Id;
    }

    private int Photo(string relativePath, int albumId)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024,
            ModifiedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IndexedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = relativePath,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();

        _db.AlbumMembers.Add(new AlbumMember { AssetId = asset.Id, AlbumId = albumId });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return asset.Id;
    }

    private void Cover(int albumId, int assetId)
    {
        Album album = _db.Albums.Single(a => a.Id == albumId);
        album.CoverAssetId = assetId;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
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
            // A locked temporary folder is not a failed test.
        }
    }
}
