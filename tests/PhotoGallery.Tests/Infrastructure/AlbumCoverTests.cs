using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Which photograph an album shows for itself, against a real SQLite file.
/// </summary>
/// <remarks>
/// The cover was worked out and never chosen: the member with the most faces in
/// it, or the middle of the album's span where no face has been found. That is a
/// good answer and it was the only answer, and it was recalculated on every add
/// and every remove - so the interesting tests here are not about the rule but
/// about what happens to a person's choice while the rule keeps running.
/// </remarks>
public sealed class AlbumCoverTests : IDisposable
{
    private static readonly DateTime March = new(2019, 3, 3, 10, 0, 0, DateTimeKind.Unspecified);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly string _root;
    private readonly GalleryDbContext _db;

    public AlbumCoverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-covers-{Guid.NewGuid():N}");
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
    public async Task WithNobodyChoosing_TheOneWithAFaceInItIsTheCover()
    {
        int plain = Add("plain.jpg", March);
        int withFace = Add("face.jpg", March.AddHours(1));
        Face(withFace);

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [plain, withFace]);

        Assert.Equal(withFace, await CoverOfAsync(album));
    }

    [Fact]
    public async Task ChoosingOne_MakesItTheCover()
    {
        int plain = Add("plain.jpg", March);
        int withFace = Add("face.jpg", March.AddHours(1));
        Face(withFace);

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [plain, withFace]);

        Assert.True(await Repository().SetCoverAsync(album, plain));

        Assert.Equal(plain, await CoverOfAsync(album));
    }

    /// <summary>
    /// And the rule does not take it back on the next photograph.
    /// </summary>
    /// <remarks>
    /// The whole reason the choice is recorded rather than only applied. A cover
    /// is worked out again every time an album gains or loses a photograph, so
    /// without a mark saying somebody answered, a chosen cover would last
    /// exactly until the next thing was added and would then be replaced with
    /// nothing said about it.
    /// </remarks>
    [Fact]
    public async Task AndTheRuleLeavesItAloneWhenMorePhotographsArrive()
    {
        int chosen = Add("chosen.jpg", March);
        int withFace = Add("face.jpg", March.AddHours(1));
        Face(withFace);

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [chosen]);
        await Repository().SetCoverAsync(album, chosen);

        await Repository().AddAsync(album, [withFace]);

        Assert.Equal(chosen, await CoverOfAsync(album));
    }

    [Fact]
    public async Task TakingTheChosenOneOut_HandsTheQuestionBackToTheRule()
    {
        int chosen = Add("chosen.jpg", March);
        int withFace = Add("face.jpg", March.AddHours(1));
        Face(withFace);

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [chosen, withFace]);
        await Repository().SetCoverAsync(album, chosen);

        await Repository().RemoveAsync(album, [chosen]);

        // The rule again, because the choice was about a photograph this album
        // no longer holds.
        Assert.Equal(withFace, await CoverOfAsync(album));
        Assert.Null(await ChosenAtAsync(album));
    }

    [Fact]
    public async Task AnAlbumCannotShowAPhotographItDoesNotHold()
    {
        int inside = Add("inside.jpg", March);
        int outside = Add("outside.jpg", March);

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [inside]);

        Assert.False(await Repository().SetCoverAsync(album, outside));
        Assert.Equal(inside, await CoverOfAsync(album));
    }

    [Fact]
    public async Task EmptyingTheAlbum_ForgetsTheChoiceAsWellAsTheCover()
    {
        int only = Add("only.jpg", March);

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [only]);
        await Repository().SetCoverAsync(album, only);

        await Repository().RemoveAsync(album, [only]);

        Assert.Equal(0, await CoverOfAsync(album));
        Assert.Null(await ChosenAtAsync(album));
    }

    /// <summary>
    /// The album a photograph is in says which picture it shows for itself.
    /// </summary>
    /// <remarks>
    /// The test that was missing. The viewer decides whether to offer "make this
    /// the album cover" by comparing the open photograph against the cover its
    /// album reports, and the query behind that reported no cover at all - so
    /// the offer stood there after it had been taken, and pressing it looked
    /// like pressing nothing. The view-model tests passed throughout, because
    /// they built the album summary by hand and therefore tested the comparison
    /// rather than the answer it compares against.
    /// </remarks>
    [Fact]
    public async Task TheAlbumAPhotographIsIn_SaysWhichPictureItShows()
    {
        int chosen = Add("chosen.jpg", March);
        int other = Add("other.jpg", March.AddHours(1));

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [chosen, other]);
        await Repository().SetCoverAsync(album, chosen);

        AlbumSummary? found = await Repository().FindForAssetAsync(other);

        Assert.NotNull(found);
        Assert.Equal("chosen.jpg", found.CoverThumbnailName);
    }

    /// <summary>
    /// Deleting the photograph an album shows leaves it showing one it holds.
    /// </summary>
    /// <remarks>
    /// The membership row goes by cascade when the photograph does, and nothing
    /// followed it: the cover column is a plain int with no foreign key, so the
    /// album was left pointing at a row that is gone and drew a grey card - the
    /// same thing a person sees when an album has failed to arrive from another
    /// machine, and impossible to tell apart from it.
    /// </remarks>
    [Fact]
    public async Task DeletingTheCoverLeavesTheAlbumShowingOneItStillHolds()
    {
        int cover = Add("cover.jpg", March);
        int kept = Add("kept.jpg", March.AddHours(1));

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [cover, kept]);
        await Repository().SetCoverAsync(album, cover);

        await new SqliteAssetRepository(_db).RemoveAsync([cover]);

        Assert.Equal(kept, await CoverOfAsync(album));

        // And the choice is forgotten with it, so the rule owns the answer
        // again rather than guarding a photograph nobody can see.
        Assert.Null(await ChosenAtAsync(album));
    }

    /// <summary>And an album emptied by a deletion shows nothing at all.</summary>
    [Fact]
    public async Task DeletingEveryPhotographLeavesTheAlbumWithNoCover()
    {
        int only = Add("only.jpg", March);

        int album = await Repository().CreateAsync("Sunday");
        await Repository().AddAsync(album, [only]);

        await new SqliteAssetRepository(_db).RemoveAsync([only]);

        Assert.Equal(0, await CoverOfAsync(album));
    }

    private IAlbumRepository Repository() => new SqliteAlbumRepository(_db);

    private async Task<int> CoverOfAsync(int albumId)
    {
        _db.ChangeTracker.Clear();

        return await _db.Albums
            .AsNoTracking()
            .Where(album => album.Id == albumId)
            .Select(album => album.CoverAssetId)
            .SingleAsync();
    }

    private async Task<DateTime?> ChosenAtAsync(int albumId)
    {
        _db.ChangeTracker.Clear();

        return await _db.Albums
            .AsNoTracking()
            .Where(album => album.Id == albumId)
            .Select(album => album.CoverChosenUtc)
            .SingleAsync();
    }

    private int Add(string relativePath, DateTime takenUtc)
    {
        var asset = new Asset
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
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return asset.Id;
    }

    private void Face(int assetId)
    {
        _db.Faces.Add(new Face
        {
            AssetId = assetId,
            Bounds = Head,
            DetectScore = 0.9f,
            Embedding = new FaceEmbedding(new float[FaceEmbedding.Dimensions]),
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
            // A locked temporary folder is not a failed test.
        }
    }
}
