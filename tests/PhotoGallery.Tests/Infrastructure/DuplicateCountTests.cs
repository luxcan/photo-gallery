using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The number beside Duplicates in the navigation, after a copy in a group was
/// deleted somewhere other than the Duplicates screen.
/// </summary>
/// <remarks>
/// Deleting a copy from the photo viewer takes its membership row with it, and
/// the group it was in stays unresolved. The screen already hides a group that
/// has stopped being a question - fewer than two copies left - but
/// the count went on including it until the next duplicates pass, and a restart
/// did not help. Counting the groups the screen would show, rather than
/// resolving them, keeps the survivors free to be offered by a later pass: a
/// resolved group records a decision, and its copies are never asked about
/// again.
/// </remarks>
public sealed class DuplicateCountTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;
    private readonly SqliteAssetRepository _assets;
    private readonly SqliteDuplicateRepository _duplicates;

    public DuplicateCountTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-duplicate-count-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();

        _index = new SqliteLibraryIndex(_db);
        _assets = new SqliteAssetRepository(_db);
        _duplicates = new SqliteDuplicateRepository(_db);
    }

    [Fact]
    public async Task AGroupLeftWithOneCopy_IsNotCounted()
    {
        (int _, int[] redundant) = AddGroup(copies: 2);

        await _assets.RemoveAsync(redundant);

        Assert.Equal(0, (await _index.GetCountsAsync()).UnresolvedDuplicateSets);
        Assert.Empty(await _duplicates.GetAsync(DuplicateKind.Exact));
    }

    /// <summary>
    /// The copy a pass stored as keeper is not what the screen suggests: that is
    /// worked out when the screen is shown (PRP 14). Two copies left are still a
    /// question, whichever one went.
    /// </summary>
    [Fact]
    public async Task AGroupWhoseStoredKeeperWasDeleted_IsStillCounted()
    {
        (int keeper, int[] _) = AddGroup(copies: 3);

        await _assets.RemoveAsync([keeper]);

        Assert.Equal(1, (await _index.GetCountsAsync()).UnresolvedDuplicateSets);
        Assert.Single(await _duplicates.GetAsync(DuplicateKind.Exact));
    }

    /// <summary>
    /// And the count goes on agreeing with the screen for a group that is still
    /// a question.
    /// </summary>
    [Fact]
    public async Task AGroupStillAskingAQuestion_IsStillCounted()
    {
        (int _, int[] redundant) = AddGroup(copies: 3);

        await _assets.RemoveAsync([redundant[0]]);

        Assert.Equal(1, (await _index.GetCountsAsync()).UnresolvedDuplicateSets);
        Assert.Single(await _duplicates.GetAsync(DuplicateKind.Exact));
    }

    /// <summary>
    /// A keeper and the rest redundant, all unresolved, as a pass leaves them.
    /// </summary>
    private (int Keeper, int[] Redundant) AddGroup(int copies)
    {
        var assets = Enumerable.Range(0, copies)
            .Select(at => new Asset
            {
                PhotoSourceId = 1,
                RelativePath = $@"copies\{at}.jpg",
                Length = 1,
                ModifiedUtc = new DateTime(2020, 1, 1),
                CreatedUtc = new DateTime(2020, 1, 1),
                IndexedUtc = new DateTime(2020, 1, 1),
                Kind = AssetKind.Photo,
                ThumbnailName = "the-same-picture",
                ContentHash = "the-same-bytes",
            })
            .ToList();

        _db.Assets.AddRange(assets);
        _db.SaveChanges();

        var set = new DuplicateSet { Kind = DuplicateKind.Exact, DetectedUtc = new DateTime(2020, 1, 2) };
        for (int at = 0; at < copies; at++)
        {
            set.Members.Add(new DuplicateMember
            {
                AssetId = assets[at].Id,
                Role = at == 0 ? DuplicateRole.Keeper : DuplicateRole.Redundant,
                Distance = at,
            });
        }

        _db.Set<DuplicateSet>().Add(set);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return (assets[0].Id, [.. assets.Skip(1).Select(asset => asset.Id)]);
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
            // A temporary folder; SQLite's pooled handle may outlive the context.
        }
    }
}
