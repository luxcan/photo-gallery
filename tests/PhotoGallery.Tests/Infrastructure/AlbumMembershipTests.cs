using Microsoft.EntityFrameworkCore;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The two rules that live in the schema rather than in a handler.
/// </summary>
/// <remarks>
/// A photograph belongs to at most one album, and a rejection outlives the
/// row it was made against. Both are asserted against a real SQLite file with
/// the migrations applied, because both are claims about the database: the
/// point of a primary key is that it holds whichever handler forgets, and no
/// in-memory model would prove it.
/// </remarks>
public sealed class AlbumMembershipTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;

    public AlbumMembershipTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-albums-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();
    }

    [Fact]
    public async Task OnePhotographCannotBeInTwoAlbumsAtOnce()
    {
        int asset = Add("a.jpg");
        int first = Collect("Genting Trip", "2019-03-03..2019-03-05");
        int second = Collect("March 2019", "2019-03-20..2019-03-20");

        _db.AlbumMembers.Add(new AlbumMember { AssetId = asset, AlbumId = first });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _db.AlbumMembers.Add(new AlbumMember { AssetId = asset, AlbumId = second });

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task MovingAPhotographIsOneDeleteAndOneInsert()
    {
        // The shape every write has to take, because the key refuses a second
        // insert rather than overwriting it.
        int asset = Add("b.jpg");
        int first = Collect("Genting Trip", "2019-03-03..2019-03-05");
        int second = Collect("March 2019", "2019-03-20..2019-03-20");

        _db.AlbumMembers.Add(new AlbumMember { AssetId = asset, AlbumId = first });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _db.AlbumMembers.Remove(new AlbumMember { AssetId = asset, AlbumId = first });
        await _db.SaveChangesAsync();
        _db.AlbumMembers.Add(new AlbumMember { AssetId = asset, AlbumId = second });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        AlbumMember member = await _db.AlbumMembers.AsNoTracking().SingleAsync();
        Assert.Equal(second, member.AlbumId);
    }

    [Fact]
    public async Task ARejectionSurvivesTheAlbumRowBeingRebuilt()
    {
        // The whole reason a rejection is keyed on the span. A rebuild deletes
        // and reinserts every proposed row, so anything remembered against the
        // id would be forgotten the next time the library is scanned.
        int asset = Add("c.jpg");
        const string span = "2019-03-03..2019-03-05";
        int proposed = Collect("Genting Trip", span);

        _db.AlbumRejections.Add(new AlbumRejection
        {
            AssetId = asset,
            ProposalKey = span,
            RejectedUtc = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc),
        });
        await _db.SaveChangesAsync();

        _db.Albums.Remove(await _db.Albums.SingleAsync(c => c.Id == proposed));
        await _db.SaveChangesAsync();
        Collect("Genting Trip", span);
        _db.ChangeTracker.Clear();

        Assert.True(await _db.AlbumRejections.AnyAsync(
            r => r.AssetId == asset && r.ProposalKey == span));
    }

    [Fact]
    public async Task ARejectionInOneSpanSaysNothingAboutAnother()
    {
        int asset = Add("d.jpg");
        _db.AlbumRejections.Add(new AlbumRejection
        {
            AssetId = asset,
            ProposalKey = "2019-03-03..2019-03-05",
            RejectedUtc = DateTime.UnixEpoch,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.False(await _db.AlbumRejections.AnyAsync(
            r => r.AssetId == asset && r.ProposalKey == "2019-07-01..2019-07-03"));
    }

    [Fact]
    public async Task DeletingAPhotographTakesItsMembershipAndItsRejectionWithIt()
    {
        // Otherwise an orphan rejection names a photograph that is not there,
        // and it can never be undone because nothing shows it.
        int asset = Add("e.jpg");
        int album = Collect("March 2019", "2019-03-20..2019-03-20");

        _db.AlbumMembers.Add(
            new AlbumMember { AssetId = asset, AlbumId = album });
        _db.AlbumRejections.Add(new AlbumRejection
        {
            AssetId = asset,
            ProposalKey = "2019-04-01..2019-04-02",
            RejectedUtc = DateTime.UnixEpoch,
        });
        await _db.SaveChangesAsync();

        _db.Assets.Remove(await _db.Assets.SingleAsync(a => a.Id == asset));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Empty(await _db.AlbumMembers.ToListAsync());
        Assert.Empty(await _db.AlbumRejections.ToListAsync());
    }

    [Fact]
    public async Task TwoAlbumsCannotClaimTheSameRunOfDays()
    {
        Collect("Genting Trip", "2019-03-03..2019-03-05");
        _db.ChangeTracker.Clear();

        _db.Albums.Add(NewAlbum("Genting Again", "2019-03-03..2019-03-05"));

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task AnAlbumSomebodyMadeCarriesNoSpanKeyAtAll()
    {
        // Several of them, to prove the unique index tolerates it: a made
        // album is not a proposal and has no run of days behind it.
        _db.Albums.Add(NewAlbum("Favourites", proposalKey: null));
        _db.Albums.Add(NewAlbum("To print", proposalKey: null));
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Assert.Equal(2, await _db.Albums.CountAsync(c => c.ProposalKey == null));
    }

    private int Add(string relativePath)
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
        _db.ChangeTracker.Clear();

        return asset.Id;
    }

    private int Collect(string name, string? proposalKey)
    {
        Album album = NewAlbum(name, proposalKey);
        _db.Albums.Add(album);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return album.Id;
    }

    private static Album NewAlbum(string name, string? proposalKey) => new()
    {
        Name = name,
        StartUtc = new DateTime(2019, 3, 3, 12, 0, 0, DateTimeKind.Unspecified),
        EndUtc = new DateTime(2019, 3, 5, 18, 0, 0, DateTimeKind.Unspecified),
        Kind = AlbumKind.Event,
        Origin = proposalKey is null ? AlbumOrigin.Made : AlbumOrigin.Proposed,
        ProposalKey = proposalKey,
        BuiltUtc = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc),
    };

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
