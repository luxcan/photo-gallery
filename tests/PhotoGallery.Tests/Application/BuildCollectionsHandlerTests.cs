using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Collections;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Collections;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The grouping pass, and the three promises it makes to the user.
/// </summary>
/// <remarks>
/// A rejection is remembered for that photograph in that span and nowhere else;
/// a collection somebody made or kept is never touched by a rebuild; and
/// rebuilding after a folder is added does not duplicate what is already there.
///
/// <para>Against a real SQLite file rather than doubles, because two of the
/// three are claims about what survives being written down - and a fake
/// repository that forgets in the same way the real one might would prove
/// nothing.</para>
/// </remarks>
public sealed class BuildCollectionsHandlerTests : IDisposable
{
    private static readonly DateTime Trip =
        new(2019, 3, 3, 10, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task AWeekendOfPhotographsIsOfferedAsOneCollection()
    {
        AddDays(Trip, days: 3, perDay: 6);

        CollectionsResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Proposed);
        Assert.Equal(18, result.Grouped);

        Collection collection = await _db.Collections.Include(c => c.Members).SingleAsync();
        Assert.Equal(CollectionOrigin.Proposed, collection.Origin);
        Assert.Equal("2019-03-03..2019-03-05", collection.ProposalKey);
        Assert.Equal(18, collection.Members.Count);
        Assert.NotEqual(0, collection.CoverAssetId);
    }

    [Fact]
    public async Task RunningItTwiceOverTheSamePhotographsChangesNothing()
    {
        AddDays(Trip, days: 3, perDay: 6);

        await NewHandler().HandleAsync();
        int firstId = await _db.Collections.Select(c => c.Id).SingleAsync();
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Collection collection = await _db.Collections.Include(c => c.Members).SingleAsync();
        Assert.Equal(firstId, collection.Id);
        Assert.Equal(18, collection.Members.Count);
    }

    [Fact]
    public async Task DismissingACollectionKeepsItDismissed()
    {
        // The requirement in one test: reject, rebuild, and it must not come
        // back. It works without a dismissed flag because the photographs are
        // remembered against the span, and what is left no longer earns a place.
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        int id = await _db.Collections.Select(c => c.Id).SingleAsync();
        await Repository().DismissAsync(id);
        _db.ChangeTracker.Clear();

        CollectionsResult again = await NewHandler().HandleAsync();

        Assert.Equal(0, again.Proposed);
        Assert.Empty(await _db.Collections.ToListAsync());
        Assert.Equal(18, await _db.CollectionRejections.CountAsync());
    }

    [Fact]
    public async Task RejectingOnePhotographLeavesTheRestOfTheCollectionAlone()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        int id = await _db.Collections.Select(c => c.Id).SingleAsync();
        int unwanted = await _db.CollectionMembers
            .Where(m => m.CollectionId == id).Select(m => m.AssetId).FirstAsync();

        await Repository().RemoveAsync(id, [unwanted]);
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Collection collection = await _db.Collections.Include(c => c.Members).SingleAsync();
        Assert.Equal(17, collection.Members.Count);
        Assert.DoesNotContain(collection.Members, m => m.AssetId == unwanted);
    }

    [Fact]
    public async Task ARejectionInOneSpanDoesNotFollowThePhotographToAnother()
    {
        // "That photo, that album" - the photograph is refused for those days
        // and stays available for every other occasion.
        AddDays(Trip, days: 3, perDay: 6);
        AddDays(Trip.AddDays(60), days: 2, perDay: 6);
        await NewHandler().HandleAsync();

        Collection first = await _db.Collections
            .Include(c => c.Members)
            .OrderBy(c => c.StartUtc)
            .FirstAsync();
        int unwanted = first.Members.First().AssetId;
        await Repository().RemoveAsync(first.Id, [unwanted]);
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Assert.Equal(2, await _db.Collections.CountAsync());
        Assert.Single(await _db.CollectionRejections.ToListAsync());
    }

    [Fact]
    public async Task ACollectionSomebodyMadeIsNeverTouchedByARebuild()
    {
        AddDays(Trip, days: 3, perDay: 6);

        ICollectionRepository repository = Repository();
        int mine = await repository.CreateAsync("To print");
        int[] taken = await _db.Assets.Select(a => a.Id).Take(4).ToArrayAsync();
        await repository.AddAsync(mine, taken);
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Collection kept = await _db.Collections
            .Include(c => c.Members)
            .SingleAsync(c => c.Id == mine);

        Assert.Equal("To print", kept.Name);
        Assert.Equal(CollectionOrigin.Made, kept.Origin);
        Assert.Equal(4, kept.Members.Count);

        // And the photographs it holds were not offered to anything else: a
        // photograph belongs to at most one collection.
        Collection proposed = await _db.Collections
            .Include(c => c.Members)
            .SingleAsync(c => c.Origin == CollectionOrigin.Proposed);
        Assert.DoesNotContain(proposed.Members, m => taken.Contains(m.AssetId));
    }

    [Fact]
    public async Task ANameTheUserTypedSurvivesARebuild()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        int id = await _db.Collections.Select(c => c.Id).SingleAsync();
        await Repository().RenameAsync(id, "Genting, at last");
        _db.ChangeTracker.Clear();

        await NewHandler().HandleAsync();

        Assert.Equal("Genting, at last", await _db.Collections.Select(c => c.Name).SingleAsync());
    }

    [Fact]
    public async Task PhotographsWithNoCaptureDateAreLeftOutRatherThanMisfiled()
    {
        AddDays(Trip, days: 3, perDay: 6);
        for (int i = 0; i < 5; i++)
        {
            Add($"undated{i}.jpg", takenUtc: null);
        }

        CollectionsResult result = await NewHandler().HandleAsync();

        Assert.Equal(18, result.Considered);
        Assert.Equal(18, result.Grouped);
    }

    [Fact]
    public async Task ALibraryWithNothingToGroupSaysSoRatherThanFailing()
    {
        CollectionsResult result = await NewHandler().HandleAsync();

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

        CollectionsResult result = await NewHandler().HandleAsync(
            cancellationToken: cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Empty(await _db.Collections.ToListAsync());
    }

    [Fact]
    public async Task MovingAPhotographSaysWhichCollectionItCameOutOf()
    {
        AddDays(Trip, days: 3, perDay: 6);
        await NewHandler().HandleAsync();

        ICollectionRepository repository = Repository();
        Collection proposed = await _db.Collections.Include(c => c.Members).SingleAsync();
        string was = proposed.Name;
        int[] moving = [.. proposed.Members.Take(2).Select(m => m.AssetId)];

        int mine = await repository.CreateAsync("To print");
        CollectionMoveResult moved = await repository.AddAsync(mine, moving);

        Assert.Equal(2, moved.Added);
        Assert.Equal(2, moved.Moved);
        Assert.Equal(was, Assert.Single(moved.From));
    }

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private int _nextDay;

    public BuildCollectionsHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-build-collections-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();
    }

    private BuildCollectionsHandler NewHandler() =>
        new(Repository(), new SqliteCollectionFactsReader(_db));

    private ICollectionRepository Repository() => new SqliteCollectionRepository(_db);

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
