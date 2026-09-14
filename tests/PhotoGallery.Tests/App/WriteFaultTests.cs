using Microsoft.EntityFrameworkCore;
using PhotoGallery.App.Shell;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// What a failed write says about itself, in the log.
/// </summary>
/// <remarks>
/// A scan that failed twice said only that some write expected one row and
/// found none. The method came from the stack; the row came from nowhere, and a
/// day went on guessing which one it was. The exception had been carrying the
/// answer the whole time.
///
/// <para>The failure here is raised the way the real one was - a tracked
/// membership whose row is deleted underneath it - rather than by constructing
/// an exception by hand, because what is being pinned is that this reads what
/// Entity Framework actually puts in one.</para>
/// </remarks>
public sealed class WriteFaultTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;

    public WriteFaultTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-faults-{Guid.NewGuid():N}");
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
    public async Task AWriteThatLostItsRow_SaysWhichRow()
    {
        int asset = Add("gone.jpg");

        _db.Albums.Add(new Album
        {
            Name = "A day out",
            StartUtc = DateTime.UnixEpoch,
            EndUtc = DateTime.UnixEpoch,
            Kind = AlbumKind.Event,
            Origin = AlbumOrigin.Proposed,
            ProposalKey = "days:one",
            BuiltUtc = DateTime.UnixEpoch,
            Members = { new AlbumMember { AssetId = asset, AddedUtc = DateTime.UnixEpoch } },
        });

        await _db.SaveChangesAsync();

        // Tracked, then deleted underneath by a statement the tracker never
        // hears about - which is what an earlier phase of a scan does.
        AlbumMember tracked = await _db.AlbumMembers.SingleAsync();
        await _db.Assets.Where(row => row.Id == asset).ExecuteDeleteAsync();

        _db.AlbumMembers.Remove(tracked);

        DbUpdateConcurrencyException lost =
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => _db.SaveChangesAsync());

        string rows = WriteFault.Rows(lost);

        Assert.Contains("AlbumMember", rows, StringComparison.Ordinal);
        Assert.Contains("Deleted", rows, StringComparison.Ordinal);
        Assert.Contains($"AssetId={asset}", rows, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureThatIsNotAWrite_HasNoRowsToName()
    {
        // Every other kind of fault reaches the same catch, and this must add
        // nothing to the log rather than an empty heading.
        Assert.Empty(WriteFault.Rows(new IOException("the folder went away")));
    }

    private int Add(string relativePath)
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024,
            ModifiedUtc = DateTime.UnixEpoch,
            CreatedUtc = DateTime.UnixEpoch,
            IndexedUtc = DateTime.UnixEpoch,
            TakenUtc = DateTime.UnixEpoch,
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
