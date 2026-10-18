using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The grouping pass, and the three promises it makes to the user.
/// </summary>
/// <remarks>
/// A rejection is remembered for that photograph in that span and nowhere else;
/// an album somebody made or kept is never touched by a rebuild; and
/// rebuilding after a folder is added does not duplicate what is already there.
///
/// <para>Against a real SQLite file rather than doubles, because two of the
/// three are claims about what survives being written down - and a fake
/// repository that forgets in the same way the real one might would prove
/// nothing.</para>
/// </remarks>
public sealed class BuildAlbumsHandlerTests : IDisposable
{
    private static readonly DateTime Trip =
        new(2019, 3, 3, 10, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task AWeekendOfPhotographsIsOfferedAsOneAlbum()
    {
        AddDays(Trip, days: 3, perDay: 6);

        AlbumsResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Proposed);
        Assert.Equal(18, result.Grouped);

        Album album = await _db.Albums.Include(c => c.Members).SingleAsync();
        Assert.Equal(AlbumOrigin.Proposed, album.Origin);
        Assert.Equal("2019-03-03..2019-03-05", album.ProposalKey);
        Assert.Equal(18, album.Members.Count);
        Assert.NotEqual(0, album.CoverAssetId);
    }

    [Fact]
    public async Task TheCoverIsAPhotographWithSomebodyInIt()
    {
        // Left to the middle of the span alone, the covers on a real library
        // came out as a hotel blanket and a ceiling: each genuinely the middle
        // photograph, and none of them any use for recognising the holiday.
        AddDays(Trip, days: 3, perDay: 6);
        int[] ids = [.. _db.Assets.OrderBy(a => a.Id).Select(a => a.Id)];
        int withPeople = ids[^2];
        AddFace(withPeople);

        await NewHandler().HandleAsync();

        Assert.Equal(withPeople, await _db.Albums.Select(c => c.CoverAssetId).SingleAsync());
    }

    [Fact]
    public async Task RunningItTwiceOverTheSamePhotographsChangesNothing()
    {
        AddDays(Trip, days: 3, perDay: 6);

        await NewHandler().HandleAsync();
        int firstId = await _db.Albums.Select(c => c.Id).SingleAsync();
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Album album = await _db.Albums.Include(c => c.Members).SingleAsync();
        Assert.Equal(firstId, album.Id);
        Assert.Equal(18, album.Members.Count);
    }

    [Fact]
    public async Task DismissingAnAlbumKeepsItDismissed()
    {
        // The requirement in one test: reject, rebuild, and it must not come
        // back. It works without a dismissed flag because the photographs are
        // remembered against the span, and what is left no longer earns a place.
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        int id = await _db.Albums.Select(c => c.Id).SingleAsync();
        await Repository().DismissAsync(id);
        _db.ChangeTracker.Clear();

        AlbumsResult again = await NewHandler().HandleAsync();

        Assert.Equal(0, again.Proposed);
        Assert.Empty(await _db.Albums.ToListAsync());
        Assert.Equal(18, await _db.AlbumRejections.CountAsync());
    }

    [Fact]
    public async Task RejectingOnePhotographLeavesTheRestOfTheAlbumAlone()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        int id = await _db.Albums.Select(c => c.Id).SingleAsync();
        int unwanted = await _db.AlbumMembers
            .Where(m => m.AlbumId == id).Select(m => m.AssetId).FirstAsync();

        await Repository().RemoveAsync(id, [unwanted]);
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Album album = await _db.Albums.Include(c => c.Members).SingleAsync();
        Assert.Equal(17, album.Members.Count);
        Assert.DoesNotContain(album.Members, m => m.AssetId == unwanted);
    }

    [Fact]
    public async Task ARejectionInOneSpanDoesNotFollowThePhotographToAnother()
    {
        // "That photo, that album" - the photograph is refused for those days
        // and stays available for every other occasion.
        AddDays(Trip, days: 3, perDay: 6);
        AddDays(Trip.AddDays(60), days: 2, perDay: 6);
        await NewHandler().HandleAsync();

        Album first = await _db.Albums
            .Include(c => c.Members)
            .OrderBy(c => c.StartUtc)
            .FirstAsync();
        int unwanted = first.Members.First().AssetId;
        await Repository().RemoveAsync(first.Id, [unwanted]);
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Assert.Equal(2, await _db.Albums.CountAsync());
        Assert.Single(await _db.AlbumRejections.ToListAsync());
    }

    [Fact]
    public async Task AnAlbumSomebodyMadeIsNeverTouchedByARebuild()
    {
        AddDays(Trip, days: 3, perDay: 6);

        IAlbumRepository repository = Repository();
        int mine = await repository.CreateAsync("To print");
        int[] taken = await _db.Assets.Select(a => a.Id).Take(4).ToArrayAsync();
        await repository.AddAsync(mine, taken);
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Album kept = await _db.Albums
            .Include(c => c.Members)
            .SingleAsync(c => c.Id == mine);

        Assert.Equal("To print", kept.Name);
        Assert.Equal(AlbumOrigin.Made, kept.Origin);
        Assert.Equal(4, kept.Members.Count);

        // And the photographs it holds were not offered to anything else: a
        // photograph belongs to at most one album.
        Album proposed = await _db.Albums
            .Include(c => c.Members)
            .SingleAsync(c => c.Origin == AlbumOrigin.Proposed);
        Assert.DoesNotContain(proposed.Members, m => taken.Contains(m.AssetId));
    }

    /// <summary>
    /// A suggestion found on a shelf is kept by the next rebuild rather than
    /// removed, and is the user's from then on.
    /// </summary>
    /// <remarks>
    /// Putting an album on a shelf accepts it on the way in, so nothing writes
    /// a row like this any more - but a library published before that rule
    /// existed already holds them, and no migration can tell them apart from a
    /// proposal nobody answered. The rebuild is where they are healed, because
    /// the removal below is a delete rather than a tombstone: without this, the
    /// next scan takes an album off the collection somebody filled and leaves
    /// nothing to put back. Written directly for that reason - the way it came
    /// about is a version of the app that is no longer here.
    /// </remarks>
    [Fact]
    public async Task ASuggestionOnAShelfIsKeptByARebuildRatherThanRemoved()
    {
        Collection holiday = AddShelf("Holiday");

        var shelved = new Album
        {
            Name = "March 2019",
            StartUtc = Trip,
            EndUtc = Trip,
            Kind = AlbumKind.Period,
            Origin = AlbumOrigin.Proposed,
            ProposalKey = "2019-03-03..2019-03-03",
            BuiltUtc = DateTime.UtcNow,
            CollectionId = holiday.Id,
        };

        _db.Albums.Add(shelved);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        // Nothing offered, so its key is one the clusterer no longer makes.
        await Repository().SaveProposalsAsync([]);
        _db.ChangeTracker.Clear();

        Album kept = await _db.Albums.SingleAsync(c => c.Id == shelved.Id);
        Assert.Equal(holiday.Id, kept.CollectionId);
        Assert.Equal(AlbumOrigin.Accepted, kept.Origin);
    }

    /// <summary>
    /// The same repair on the row a rebuild does not find stale: a shelved
    /// suggestion whose photographs are untouched, so the clusterer offers its
    /// key again.
    /// </summary>
    /// <remarks>
    /// This is the shape such a library actually holds. Putting an album on a
    /// shelf changes no photograph, so the key that made it is the key the next
    /// scan makes, and a repair that only reached keys the clusterer had stopped
    /// offering would pass over the row for ever - leaving the collection
    /// counting an album its wall will not show, and nothing on the screen to
    /// say why.
    /// </remarks>
    [Fact]
    public async Task AShelvedSuggestionIsKeptEvenWhileTheClustererStillOffersIt()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();
        _db.ChangeTracker.Clear();

        Collection holiday = AddShelf("Holiday");

        // Straight to the column, the way a version with no rule about origins
        // wrote it. Going through the collection repository would keep the
        // album on the way in, which is the state this test has to start before.
        Album shelved = await _db.Albums.SingleAsync();
        shelved.CollectionId = holiday.Id;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();
        _db.ChangeTracker.Clear();

        Album kept = await _db.Albums.Include(c => c.Members).SingleAsync();
        Assert.Equal(AlbumOrigin.Accepted, kept.Origin);
        Assert.Equal(holiday.Id, kept.CollectionId);
        Assert.Equal(18, kept.Members.Count);

        // And the two numbers the user is shown agree. The band counts every
        // album carrying the shelf; the wall is filled from the albums that are
        // not proposals, so a row left as one is a count with nothing under it.
        CollectionSummary band = Assert.Single(
            await new SqliteCollectionRepository(_db).GetAsync());
        int onTheWall = await _db.Albums.CountAsync(album =>
            album.CollectionId == holiday.Id && album.Origin != AlbumOrigin.Proposed);

        Assert.Equal(band.AlbumCount, onTheWall);
    }

    [Fact]
    public async Task ANameTheUserTypedSurvivesARebuild()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        int id = await _db.Albums.Select(c => c.Id).SingleAsync();
        await Repository().RenameAsync(id, "Genting, at last");
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Assert.Equal("Genting, at last", await _db.Albums.Select(c => c.Name).SingleAsync());
    }

    /// <summary>
    /// Deleting a suggestion somebody kept is an answer about those days, and
    /// every rebuild afterwards honours it.
    /// </summary>
    /// <remarks>
    /// The tombstone keeps its run of days, which is how the deletion reaches
    /// the other machines, and its photographs are let go. Those photographs
    /// are the very ones the clusterer grouped into that run in the first
    /// place, so the next scan offers the same key again - and the rebuild,
    /// which could not see the tombstone, added a second row under it. The
    /// key is unique whether or not a row is deleted, so that failed the whole
    /// last phase of the scan, and of every scan after it.
    /// </remarks>
    [Fact]
    public async Task AKeptSuggestionSomebodyDeletedIsNeverSuggestedAgain()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        IAlbumRepository repository = Repository();
        int id = await _db.Albums.Select(c => c.Id).SingleAsync();
        await repository.AcceptAsync(id);
        await repository.DeleteAsync(id);
        _db.ChangeTracker.Clear();

        AlbumsResult again = await NewHandler().HandleAsync();

        Assert.Equal(0, again.Proposed);
        Assert.Empty(await _db.Albums.ToListAsync());
        Assert.Empty(await _db.AlbumMembers.ToListAsync());

        // And on the scan after that, which is where a failure that only
        // happened once would have hidden.
        await NewHandler().HandleAsync();

        Assert.Empty(await _db.Albums.ToListAsync());
        Assert.Equal(
            "2019-03-03..2019-03-05",
            await _db.Albums.IgnoreQueryFilters().Select(c => c.ProposalKey).SingleAsync());
    }

    /// <summary>
    /// A kept suggestion is left exactly as it was kept when its days gain
    /// photographs later, and those days are not suggested a second time.
    /// </summary>
    /// <remarks>
    /// Photographs copied over after the trip was kept group into the same run
    /// of days, and the rebuild, which read only its own suggestions, added them
    /// as a new row under a key the kept album already held - which the
    /// database refused, and the scan with it.
    ///
    /// <para>They are not put into the kept album either, though keeping
    /// promises they will be. The rebuild would be the one deciding, at the
    /// moment it ran, and an album somebody kept sends its photographs to the
    /// other machines as decisions: a scan here would outrank somebody on
    /// another laptop who had filed one of them somewhere else first. Until a
    /// photograph the app placed loses to every one a person placed, the
    /// rebuild writes nothing to an album somebody kept - which is also what
    /// the screen tells them when they keep it.</para>
    /// </remarks>
    [Fact]
    public async Task AKeptSuggestionIsLeftAsItWasKeptWhenItsDaysGainPhotographs()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        Album proposed = await _db.Albums.Include(c => c.Members).AsNoTracking().SingleAsync();
        await Repository().AcceptAsync(proposed.Id);
        _db.ChangeTracker.Clear();

        // Copied over afterwards: the same three days, and enough of them to be
        // an occasion on their own.
        AddDays(Trip.AddMinutes(20), days: 3, perDay: 3);

        AlbumsResult again = await NewHandler().HandleAsync();

        Album kept = await _db.Albums.Include(c => c.Members).SingleAsync();
        Assert.Equal(proposed.Id, kept.Id);
        Assert.Equal(AlbumOrigin.Accepted, kept.Origin);
        Assert.Equal(proposed.Name, kept.Name);
        Assert.Equal(proposed.CoverAssetId, kept.CoverAssetId);
        Assert.Equal(
            proposed.Members.Select(m => m.AssetId).Order(),
            kept.Members.Select(m => m.AssetId).Order());
        Assert.Equal(0, again.Proposed);
    }

    /// <summary>
    /// A rebuild leaves a cover somebody chose alone, for as long as the album
    /// still holds that photograph.
    /// </summary>
    /// <remarks>
    /// The rebuild wrote the clusterer's pick over every suggestion's cover and
    /// left the date saying somebody had chosen it - so the app's guess went on
    /// being published as a person's choice, under their moment, and could beat
    /// a real choice on another laptop. Once the chosen photograph has left the
    /// album the clusterer's pick is the better answer, and the choice is
    /// forgotten, which is what adding or removing a photograph already does.
    /// </remarks>
    [Fact]
    public async Task ARebuildLeavesAChosenCoverAlone()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        Album proposed = await _db.Albums.Include(c => c.Members).AsNoTracking().SingleAsync();
        int[] members = [.. proposed.Members.Select(m => m.AssetId).Order()];
        int chosen = members[0];
        int picked = members[^1];

        Assert.True(await Repository().SetCoverAsync(proposed.Id, chosen));
        _db.ChangeTracker.Clear();

        await Repository().SaveProposalsAsync([Offered(proposed, members, picked)]);
        _db.ChangeTracker.Clear();

        Album rebuilt = await _db.Albums.AsNoTracking().SingleAsync();
        Assert.Equal(chosen, rebuilt.CoverAssetId);
        Assert.NotNull(rebuilt.CoverChosenUtc);

        await Repository().SaveProposalsAsync([Offered(proposed, members[1..], picked)]);
        _db.ChangeTracker.Clear();

        rebuilt = await _db.Albums.AsNoTracking().SingleAsync();
        Assert.Equal(picked, rebuilt.CoverAssetId);
        Assert.Null(rebuilt.CoverChosenUtc);
    }

    [Fact]
    public async Task PhotographsWithNoCaptureDateAreLeftOutRatherThanMisfiled()
    {
        AddDays(Trip, days: 3, perDay: 6);
        for (int i = 0; i < 5; i++)
        {
            Add($"undated{i}.jpg", takenUtc: null);
        }

        AlbumsResult result = await NewHandler().HandleAsync();

        Assert.Equal(18, result.Considered);
        Assert.Equal(18, result.Grouped);
    }

    [Fact]
    public async Task ALibraryWithNothingToGroupSaysSoRatherThanFailing()
    {
        AlbumsResult result = await NewHandler().HandleAsync();

        Assert.Equal(0, result.Proposed);
        Assert.False(result.WasCancelled);
        Assert.Equal(string.Empty, result.Summary);
    }

    [Fact]
    public async Task StoppedAsItBeginsItAnswersRatherThanThrowing()
    {
        AddDays(Trip, days: 3, perDay: 6);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        AlbumsResult result = await NewHandler().HandleAsync(
            cancellationToken: cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Empty(await _db.Albums.ToListAsync());
    }

    [Fact]
    public async Task MovingAPhotographSaysWhichAlbumItCameOutOf()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        IAlbumRepository repository = Repository();
        Album proposed = await _db.Albums.Include(c => c.Members).SingleAsync();
        string was = proposed.Name;
        int[] moving = [.. proposed.Members.Take(2).Select(m => m.AssetId)];

        int mine = await repository.CreateAsync("To print");
        AlbumAddResult moved = await repository.AddAsync(mine, moving);

        Assert.Equal(2, moved.Added);
        Assert.Equal(2, moved.Moved);
        Assert.Equal(was, Assert.Single(moved.From));
    }

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private int _nextDay;

    public BuildAlbumsHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-build-albums-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();
    }

    private BuildAlbumsHandler NewHandler() =>
        new(Repository(), new SqliteAlbumFactsReader(_db));

    private IAlbumRepository Repository() => new SqliteAlbumRepository(_db);

    /// <summary>The same run of days offered again, holding these photographs.</summary>
    private static ProposedAlbum Offered(Album row, IReadOnlyList<int> assetIds, int cover) =>
        new(row.ProposalKey!, row.Name, row.StartUtc, row.EndUtc, row.Kind, row.PlaceId, cover, assetIds);

    /// <summary>An occasion: several days running, several photographs a day.</summary>
    private void AddDays(DateTime start, int days, int perDay)
    {
        for (int day = 0; day < days; day++)
        {
            for (int i = 0; i < perDay; i++)
            {
                Add(
                    $"d{_nextDay++}.jpg",
                    start.AddDays(day).AddMinutes(i * 40));
            }
        }
    }

    /// <summary>A collection to put an album on.</summary>
    private Collection AddShelf(string name)
    {
        var collection = new Collection
        {
            Name = name,
            CreatedUtc = DateTime.UtcNow,
            NamedUtc = DateTime.UtcNow,
        };

        _db.Collections.Add(collection);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return collection;
    }

    /// <summary>Puts one face on a photograph, so it can be a cover.</summary>
    private void AddFace(int assetId)
    {
        _db.Faces.Add(new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(10, 10, 40, 40),
            DetectScore = 0.9f,
            Embedding = new FaceEmbedding(new float[FaceEmbedding.Dimensions]),
        });

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private void Add(string relativePath, DateTime? takenUtc)
    {
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024,
            ModifiedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IndexedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TakenUtc = takenUtc,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = relativePath,
        });

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
            // A temporary folder left behind is not a failed test.
        }
    }
}
