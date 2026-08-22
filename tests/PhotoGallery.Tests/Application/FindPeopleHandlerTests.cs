using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

public sealed class FindPeopleHandlerTests : IDisposable
{
    private static readonly DateTime s_when = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly FindPeopleHandler _handler;

    private int _nextAsset = 1;

    public FindPeopleHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-find-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _tempRoot });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _handler = new FindPeopleHandler(new SqlitePeopleReader(_db));
    }

    [Fact]
    public async Task Find_WithNothingTypedOffersEveryoneBiggestFirst()
    {
        // Clicking into an empty box should answer "who can I look for?".
        Name("Ana Lim", pictures: 4);
        Name("Grandma", pictures: 9);

        IReadOnlyList<PersonDirectoryEntry> matches = await _handler.HandleAsync(null);

        Assert.Equal(["Grandma", "Ana Lim"], matches.Select(match => match.DisplayName));
    }

    [Fact]
    public async Task Find_PrefersANameThatStartsWithWhatWasTyped()
    {
        // Typing "ana" for a library holding Ana Lim and a Diana photographed
        // twenty times more often should still lead with Ana Lim. Both match, so
        // it is the ranking being tested and not the filter.
        Name("Ana Lim", pictures: 2);
        Name("Diana", pictures: 40);

        IReadOnlyList<PersonDirectoryEntry> matches = await _handler.HandleAsync("ana");

        Assert.Equal(2, matches.Count);
        Assert.Equal("Ana Lim", matches[0].DisplayName);
        Assert.Equal("Diana", matches[1].DisplayName);
    }

    [Fact]
    public async Task Find_DoesNotCareAboutCase()
    {
        Name("Ana Reyes", pictures: 3);

        Assert.Single(await _handler.HandleAsync("ANA REYES"));
        Assert.Single(await _handler.HandleAsync("ana"));
    }

    [Fact]
    public async Task Find_OfANameNobodyHasOffersNothing()
    {
        Name("Ana Lim", pictures: 3);

        Assert.Empty(await _handler.HandleAsync("Mira"));
    }

    [Fact]
    public async Task Find_CountsPicturesRatherThanFaces()
    {
        // Two people in one photograph is one picture of each of them, and a
        // person appearing twice in one shot is still one picture.
        int person = Name("Ana Lim", pictures: 2);
        int shared = AddPhoto();
        Claim(AddFace(shared), person, AssignmentSource.Confirmed);
        Claim(AddFace(shared), person, AssignmentSource.Confirmed);

        IReadOnlyList<PersonDirectoryEntry> matches = await _handler.HandleAsync("Ana");

        Assert.Equal(3, matches[0].Photos);
    }

    [Fact]
    public async Task Find_CountsOnlyWhatWasConfirmed()
    {
        int person = Name("Ana Lim", pictures: 2);
        Claim(AddFace(AddPhoto()), person, AssignmentSource.Proposed);

        IReadOnlyList<PersonDirectoryEntry> matches = await _handler.HandleAsync("Ana");

        Assert.Equal(2, matches[0].Photos);
    }

    [Fact]
    public async Task Find_OffersNoMoreThanTheBoxCanShow()
    {
        for (int i = 0; i < FindPeopleHandler.MaxMatches + 5; i++)
        {
            Name($"Person {i:D2}", pictures: 1);
        }

        Assert.Equal(FindPeopleHandler.MaxMatches, (await _handler.HandleAsync("Person")).Count);
    }

    private int Name(string displayName, int pictures)
    {
        var person = new Person { DisplayName = displayName };
        _db.Set<Person>().Add(person);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        for (int i = 0; i < pictures; i++)
        {
            Claim(AddFace(AddPhoto()), person.Id, AssignmentSource.Confirmed);
        }

        return person.Id;
    }

    private int AddPhoto()
    {
        int assetId = _nextAsset++;
        _db.Assets.Add(new Asset
        {
            Id = assetId,
            PhotoSourceId = 1,
            RelativePath = $@"folder\photo-{assetId}.jpg",
            Length = 1,
            ModifiedUtc = s_when,
            CreatedUtc = s_when,
            IndexedUtc = s_when,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = $"t{assetId:D4}.jpg",
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return assetId;
    }

    private int AddFace(int assetId)
    {
        float[] values = new float[FaceEmbedding.Dimensions];
        values[0] = 1f;

        var face = new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(0, 0, 50, 50),
            DetectScore = 0.9f,
            Embedding = new FaceEmbedding(values),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return face.Id;
    }

    private void Claim(int faceId, int personId, AssignmentSource source)
    {
        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = faceId, PersonId = personId, Source = source,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test.
        }
    }
}
