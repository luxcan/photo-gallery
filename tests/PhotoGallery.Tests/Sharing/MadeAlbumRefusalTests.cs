using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A photograph taken out of an album somebody made stays out of that album,
/// and only that album, on every computer.
/// </summary>
/// <remarks>
/// The refusal used to be keyed by the album's row number, which means nothing
/// on another laptop: it landed on whichever album had that number there, and
/// kept the photograph out of an unrelated album's "Find photos that fit".
/// </remarks>
public sealed class MadeAlbumRefusalTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public void AMadeAlbumsKeyIsItsPublicId()
    {
        var id = Guid.Parse("0e880e62-1111-2222-3333-444455556666");

        Assert.Equal("made:0e880e62-1111-2222-3333-444455556666", AlbumRefusalKey.Made(id));
        Assert.False(AlbumRefusalKey.IsLocalOnly(AlbumRefusalKey.Made(id)));
    }

    [Theory]
    [InlineData("made:7", true)]
    [InlineData("made:12345", true)]
    [InlineData("made:12345678-1111-2222-3333-444455556666", false)]
    [InlineData("2019-03-03..2019-03-05", false)]
    [InlineData("made:", false)]
    public void OnlyARowNumberIsLocal(string key, bool local) =>
        Assert.Equal(local, AlbumRefusalKey.IsLocalOnly(key));

    [Fact]
    public async Task TakingAPhotographOut_IsRememberedUnderTheAlbumsPublicId()
    {
        Album bali = Mum.Album("Bali", Monday);
        Asset a = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Mum.Put(bali, a, Monday);

        await Mum.Albums.RemoveAsync(bali.Id, [a.Id]);

        AlbumRejection refused = Assert.Single(Mum.Db.AlbumRejections.AsNoTracking());
        Assert.Equal(AlbumRefusalKey.Made(bali.PublicId), refused.ProposalKey);
    }

    [Fact]
    public async Task TheRefusalTravels_UnderAKeyThatMeansTheSameAlbumThere()
    {
        Album bali = Mum.Album("Bali", Monday);
        Asset a = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Mum.Put(bali, a, Monday);
        await Mum.Albums.RemoveAsync(bali.Id, [a.Id]);
        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        AlbumRejection arrived = Assert.Single(Dad.Db.AlbumRejections.AsNoTracking());
        Assert.Equal(AlbumRefusalKey.Made(bali.PublicId), arrived.ProposalKey);
    }

    [Fact]
    public async Task ARefusalFromAnOlderBuild_NamingARowNumber_IsNotTaken()
    {
        // Dad has an album that happens to have the row number Mum's old build
        // names. Taken, the refusal would keep the photograph out of his.
        Album his = Dad.Album("Garden", Monday);
        Asset a = Mum.Prepared(@"2019\a.jpg", "aaa.jpg");
        Dad.Prepared(@"2019\a.jpg", "aaa.jpg");

        Mum.Db.AlbumRejections.Add(new AlbumRejection
        {
            AssetId = a.Id,
            ProposalKey = $"made:{his.Id}",
            RejectedUtc = Monday,
        });
        Mum.Db.SaveChanges();
        Mum.Db.ChangeTracker.Clear();

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        Assert.Empty(Dad.Db.AlbumRejections.AsNoTracking());
    }

    [Fact]
    public void TheUpgradeRekeysRefusalsAndDropsTheOnesWhoseAlbumHasGone()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pg-refusal-upgrade-{Guid.NewGuid():N}.db");

        try
        {
            using var db = new GalleryDbContext(
                new DbContextOptionsBuilder<GalleryDbContext>()
                    .UseSqlite($"Data Source={path}")
                    .Options);

            IMigrator migrator = db.GetService<IMigrator>();
            string before = db.Database.GetMigrations()
                .TakeWhile(name => !name.EndsWith("_KeyMadeAlbumRefusalsByPublicId", StringComparison.Ordinal))
                .Last();
            migrator.Migrate(before);

            var album = new Album { Name = "Bali" };
            var asset = new Asset
            {
                PhotoSourceId = AddSource(db),
                RelativePath = "a.jpg",
                IndexedUtc = Monday,
            };
            db.Albums.Add(album);
            db.Assets.Add(asset);
            db.SaveChanges();

            db.AlbumRejections.AddRange(
                new AlbumRejection { AssetId = asset.Id, ProposalKey = $"made:{album.Id}", RejectedUtc = Monday },
                new AlbumRejection { AssetId = asset.Id, ProposalKey = "made:9999", RejectedUtc = Monday },
                new AlbumRejection { AssetId = asset.Id, ProposalKey = "2019-03-03..2019-03-05", RejectedUtc = Monday });
            db.SaveChanges();
            db.ChangeTracker.Clear();

            migrator.Migrate();

            Assert.Equal(
                ["2019-03-03..2019-03-05", AlbumRefusalKey.Made(album.PublicId)],
                db.AlbumRejections.AsNoTracking()
                    .Select(r => r.ProposalKey)
                    .AsEnumerable()
                    .Order(StringComparer.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static int AddSource(GalleryDbContext db)
    {
        var source = new PhotoGallery.Domain.Library.PhotoSource { Path = @"C:\photos", AddedUtc = Monday };
        db.PhotoSources.Add(source);
        db.SaveChanges();
        return source.Id;
    }

    public void Dispose() => _house.Dispose();
}
