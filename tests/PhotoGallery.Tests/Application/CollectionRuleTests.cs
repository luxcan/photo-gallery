using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Places;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// A collection that knows what it is looking for.
/// </summary>
/// <remarks>
/// Dates, people and places, all ANDed: each part narrows what the last one
/// left, which is what makes the rule worth having. The two asymmetries are
/// deliberate and are what these tests mostly pin down - several people means
/// every one of them, because a photograph can hold two at once; several places
/// means any of them, because it cannot have been taken in two.
/// </remarks>
public sealed class CollectionRuleTests : IDisposable
{
    private static readonly DateTime March = new(2019, 3, 3, 10, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task ARuleThatSaysNothingSuggestsNothing()
    {
        // The whole library would be the opposite of a suggestion.
        int collection = await Repository().CreateAsync("Empty");
        Add("a.jpg", March);

        Assert.Empty(await Repository().SuggestAsync(collection));
    }

    [Fact]
    public async Task DatesAloneFindWhatWasTakenInThem()
    {
        int inside = Add("inside.jpg", March);
        Add("before.jpg", March.AddDays(-3));
        Add("after.jpg", March.AddDays(3));

        int collection = await Rule(new CollectionRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([inside], await Repository().SuggestAsync(collection));
    }

    [Fact]
    public async Task OneDayMeansThatWholeDay()
    {
        // Somebody who types one date means the day, not the instant it begins.
        int lateThatEvening = Add("evening.jpg", March.Date.AddHours(23).AddMinutes(30));

        int collection = await Rule(new CollectionRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([lateThatEvening], await Repository().SuggestAsync(collection));
    }

    [Fact]
    public async Task SeveralPeopleMeansEveryOneOfThem()
    {
        int ana = AddPerson("Ana");
        int ben = AddPerson("Ben");

        int both = Add("both.jpg", March);
        Name(both, ana);
        Name(both, ben);

        int onlyAna = Add("ana.jpg", March);
        Name(onlyAna, ana);

        int collection = await Rule(new CollectionRule(null, null, [ana, ben], []));

        Assert.Equal([both], await Repository().SuggestAsync(collection));
    }

    [Fact]
    public async Task SeveralPlacesMeansAnyOfThem()
    {
        int genting = AddPlace("Genting");
        int ipoh = AddPlace("Ipoh");
        int elsewhere = AddPlace("Elsewhere");

        int one = Add("genting.jpg", March, placeId: genting);
        int two = Add("ipoh.jpg", March, placeId: ipoh);
        Add("elsewhere.jpg", March, placeId: elsewhere);

        int collection = await Rule(new CollectionRule(null, null, [], [genting, ipoh]));

        Assert.Equal([one, two], (await Repository().SuggestAsync(collection)).Order());
    }

    [Fact]
    public async Task ThePartsNarrowEachOther()
    {
        // The point of the AND: Ana, in Genting, that March - and not Ana in
        // Genting a year later, nor Ana somewhere else that March.
        int ana = AddPerson("Ana");
        int genting = AddPlace("Genting");
        int ipoh = AddPlace("Ipoh");

        int wanted = Add("wanted.jpg", March, placeId: genting);
        Name(wanted, ana);

        int wrongPlace = Add("wrong-place.jpg", March, placeId: ipoh);
        Name(wrongPlace, ana);

        int wrongYear = Add("wrong-year.jpg", March.AddYears(1), placeId: genting);
        Name(wrongYear, ana);

        int noAna = Add("no-ana.jpg", March, placeId: genting);

        int collection = await Rule(new CollectionRule(
            DateOnly.FromDateTime(March.AddDays(-1)),
            DateOnly.FromDateTime(March.AddDays(1)),
            [ana],
            [genting]));

        Assert.Equal([wanted], await Repository().SuggestAsync(collection));
        Assert.DoesNotContain(noAna, await Repository().SuggestAsync(collection));
    }

    [Fact]
    public async Task AProposedFaceIsNotEnoughToCount()
    {
        // A proposal is a question the user has not answered. Using it to fill
        // a collection would answer it for them.
        int ana = AddPerson("Ana");
        int guessed = Add("guessed.jpg", March);
        Name(guessed, ana, AssignmentSource.Proposed);

        int collection = await Rule(new CollectionRule(null, null, [ana], []));

        Assert.Empty(await Repository().SuggestAsync(collection));
    }

    [Fact]
    public async Task WhatIsAlreadySomewhereElseIsNotOffered()
    {
        int taken = Add("taken.jpg", March);
        int free = Add("free.jpg", March);

        ICollectionRepository repository = Repository();
        int somewhereElse = await repository.CreateAsync("Somewhere else");
        await repository.AddAsync(somewhereElse, [taken]);

        int collection = await Rule(new CollectionRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([free], await Repository().SuggestAsync(collection));
    }

    [Fact]
    public async Task WhatWasTakenOutIsNotOfferedBack()
    {
        // Otherwise the button would hand back exactly what the user had just
        // rejected, every time they pressed it.
        int one = Add("one.jpg", March);
        int two = Add("two.jpg", March);

        int collection = await Rule(new CollectionRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        ICollectionRepository repository = Repository();
        await repository.AddAsync(collection, [one, two]);
        await repository.RemoveAsync(collection, [two]);

        Assert.Empty(await Repository().SuggestAsync(collection));

        // And it stays out after it has been taken out of the collection too.
        await repository.RemoveAsync(collection, [one]);
        Assert.Empty(await Repository().SuggestAsync(collection));
    }

    [Fact]
    public async Task ARuleIsReadBackAsItWasWritten()
    {
        int ana = AddPerson("Ana");
        int genting = AddPlace("Genting");
        var written = new CollectionRule(
            new DateOnly(2019, 3, 1), new DateOnly(2019, 3, 31), [ana], [genting]);

        int collection = await Rule(written);

        Assert.Equal(written, await Repository().GetRuleAsync(collection));
    }

    [Fact]
    public async Task SettingARuleReplacesTheOneBefore()
    {
        int ana = AddPerson("Ana");
        int ben = AddPerson("Ben");

        int collection = await Rule(new CollectionRule(null, null, [ana, ben], []));
        await Repository().SetRuleAsync(collection, new CollectionRule(null, null, [ben], []));

        CollectionRule now = await Repository().GetRuleAsync(collection);

        Assert.Equal([ben], now.PersonIds);
    }

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private int _nextId;

    public CollectionRuleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-rules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.SaveChanges();
    }

    private ICollectionRepository Repository() => new SqliteCollectionRepository(_db);

    /// <summary>A collection of the user's own, looking for this.</summary>
    private async Task<int> Rule(CollectionRule rule)
    {
        ICollectionRepository repository = Repository();
        int collection = await repository.CreateAsync("Looking for");
        await repository.SetRuleAsync(collection, rule);
        _db.ChangeTracker.Clear();

        return collection;
    }

    private int Add(string relativePath, DateTime takenUtc, int? placeId = null)
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
            PlaceId = placeId,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = relativePath,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return asset.Id;
    }

    private int AddPerson(string name)
    {
        var person = new Person { DisplayName = name };
        _db.People.Add(person);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return person.Id;
    }

    private int AddPlace(string name)
    {
        var place = new Place
        {
            GeoNameId = ++_nextId,
            Name = name,
            CountryCode = "MY",
            Admin1Code = "06",
            Latitude = 3.4d,
            Longitude = 101.8d,
        };

        _db.Places.Add(place);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return place.Id;
    }

    /// <summary>Puts a face on a photograph and says who it is.</summary>
    private void Name(
        int assetId, int personId, AssignmentSource source = AssignmentSource.Confirmed)
    {
        var face = new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(10, 10, 40, 40),
            DetectScore = 0.9f,
            Embedding = new FaceEmbedding(new float[FaceEmbedding.Dimensions]),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();

        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = face.Id,
            PersonId = personId,
            Source = source,
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
