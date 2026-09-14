using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Writing the pass's proposals over a context other phases have already used.
/// </summary>
/// <remarks>
/// This is the shape of a real failure, reported twice and finally caught with a
/// stack. One scope serves a whole scan, so the same context runs all eight
/// phases; some of them leave what they loaded tracked, and others delete rows
/// with ExecuteDelete, which the change tracker is never told about - as is the
/// database's own cascade when an asset goes.
///
/// <para>A query does not refresh an entity the context already tracks: it hands
/// back the instance it has. So the last phase could load an album, be given a
/// membership that no longer existed, ask for it to be deleted, and lose a
/// six-minute scan to "expected to affect 1 row(s), but actually affected
/// 0".</para>
/// </remarks>
public sealed class ProposalRewriteTests : IDisposable
{
    private static readonly DateTime March = new(2019, 3, 3, 10, 0, 0, DateTimeKind.Unspecified);

    private readonly string _root;
    private readonly GalleryDbContext _db;

    public ProposalRewriteTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-proposals-{Guid.NewGuid():N}");
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
    public async Task APhotographDeletedUnderneathTheContext_DoesNotLoseTheWholePass()
    {
        int staying = Add("staying.jpg");
        int going = Add("going.jpg");

        // The pass offers a group of two.
        await Repository().SaveProposalsAsync(
            [Proposal("days:one", staying, going)]);

        // An earlier phase of the same scan reads those memberships and leaves
        // them tracked, which several of them do.
        _ = await _db.AlbumMembers.ToListAsync();

        // And a phase after it takes a photograph out of the library the way the
        // scan does - a statement, not a tracked delete - so the database drops
        // its membership through the cascade and the tracker never hears.
        await _db.Assets.Where(asset => asset.Id == going).ExecuteDeleteAsync();

        // The pass now offers the same run of days with one photograph fewer.
        // Before the fix this threw, because the membership it asked the
        // database to delete had already gone.
        await Repository().SaveProposalsAsync([Proposal("days:one", staying)]);

        _db.ChangeTracker.Clear();

        Assert.Equal(
            [staying],
            await _db.AlbumMembers.Select(member => member.AssetId).ToListAsync());
    }

    [Fact]
    public async Task TheProposalsStillSurviveARewriteWithNothingDeleted()
    {
        // The ordinary case, so the guard above cannot pass by doing nothing.
        int first = Add("first.jpg");
        int second = Add("second.jpg");

        await Repository().SaveProposalsAsync([Proposal("days:one", first)]);
        await Repository().SaveProposalsAsync([Proposal("days:one", first, second)]);

        _db.ChangeTracker.Clear();

        Album album = await _db.Albums.Include(a => a.Members).SingleAsync();

        Assert.Equal(AlbumOrigin.Proposed, album.Origin);
        Assert.Equal([first, second], album.Members.Select(m => m.AssetId).Order());
    }

    /// <summary>
    /// A photograph moving from one proposal to another in a single pass.
    /// </summary>
    /// <remarks>
    /// The real failure, reduced. A membership is keyed by the photograph alone
    /// - one album each - so moving one between two proposals means deleting a
    /// row and inserting another with the very same key, inside one save.
    /// </remarks>
    [Fact]
    public async Task APhotographMovingBetweenTwoProposals_DoesNotLoseThePass()
    {
        int staying = Add("staying.jpg");
        int moving = Add("moving.jpg");
        int other = Add("other.jpg");

        await Repository().SaveProposalsAsync(
            [Proposal("days:one", staying, moving), Proposal("days:two", other)]);

        await Repository().SaveProposalsAsync(
            [Proposal("days:one", staying), Proposal("days:two", other, moving)]);

        _db.ChangeTracker.Clear();

        int holder = await _db.AlbumMembers
            .Where(member => member.AssetId == moving)
            .Select(member => member.AlbumId)
            .SingleAsync();

        string key = await _db.Albums
            .Where(album => album.Id == holder)
            .Select(album => album.ProposalKey!)
            .SingleAsync();

        Assert.Equal("days:two", key);
    }

    /// <summary>
    /// And when the proposal it was in is not offered at all any more.
    /// </summary>
    [Fact]
    public async Task APhotographWhoseProposalIsGone_MovesToTheOneThatWantsIt()
    {
        int moving = Add("moving.jpg");
        int other = Add("other.jpg");

        await Repository().SaveProposalsAsync([Proposal("days:one", moving)]);

        // "days:one" is no longer a run of days the pass makes, so its row goes -
        // and the photograph it held is offered by a different proposal in the
        // same breath.
        await Repository().SaveProposalsAsync([Proposal("days:two", other, moving)]);

        _db.ChangeTracker.Clear();

        Assert.Equal(
            1,
            await _db.AlbumMembers.CountAsync(member => member.AssetId == moving));
    }

    private IAlbumRepository Repository() => new SqliteAlbumRepository(_db);

    private static ProposedAlbum Proposal(string key, params int[] assetIds) =>
        new(key, "A day out", March, March.AddHours(6), AlbumKind.Event, null, assetIds[0], assetIds);

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
            TakenUtc = March,
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
            // A locked temporary folder is not a failed test.
        }
    }
}
