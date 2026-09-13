using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Places;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// An album that knows what it is looking for.
/// </summary>
/// <remarks>
/// Dates, people and places, all ANDed: each part narrows what the last one
/// left, which is what makes the rule worth having. The two asymmetries are
/// deliberate and are what these tests mostly pin down - several people means
/// every one of them, because a photograph can hold two at once; several places
/// means any of them, because it cannot have been taken in two.
/// </remarks>
public sealed class AlbumRuleTests : IDisposable
{
    private static readonly DateTime March = new(2019, 3, 3, 10, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task ARuleThatSaysNothingSuggestsNothing()
    {
        // The whole library would be the opposite of a suggestion.
        int album = await Repository().CreateAsync("Empty");
        Add("a.jpg", March);

        Assert.Empty(await Repository().SuggestAsync(album));
    }

    [Fact]
    public async Task DatesAloneFindWhatWasTakenInThem()
    {
        int inside = Add("inside.jpg", March);
        Add("before.jpg", March.AddDays(-3));
        Add("after.jpg", March.AddDays(3));

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([inside], await Repository().SuggestAsync(album));
    }

    [Fact]
    public async Task OneDayMeansThatWholeDay()
    {
        // Somebody who types one date means the day, not the instant it begins.
        int lateThatEvening = Add("evening.jpg", March.Date.AddHours(23).AddMinutes(30));

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([lateThatEvening], await Repository().SuggestAsync(album));
    }

    [Fact]
    public async Task SeveralPeopleMeansAnyOfThem()
    {
        // Every one of them was the first reading, and it made a three-name
        // album that found a single photograph: the times everybody stands
        // together are rare, and they are not what the album was about.
        int ana = AddPerson("Ana");
        int ben = AddPerson("Ben");
        int cara = AddPerson("Cara");

        int both = Add("both.jpg", March);
        Name(both, ana);
        Name(both, ben);

        int onlyAna = Add("ana.jpg", March);
        Name(onlyAna, ana);

        int onlyBen = Add("ben.jpg", March);
        Name(onlyBen, ben);

        int neither = Add("cara.jpg", March);
        Name(neither, cara);

        int album = await Rule(new AlbumRule(null, null, [ana, ben], []));

        IReadOnlyList<int> fitting = await Repository().SuggestAsync(album);

        Assert.Equal([both, onlyAna, onlyBen], fitting.Order());
        Assert.DoesNotContain(neither, fitting);
    }

    [Fact]
    public async Task PeopleAreOredWithoutLooseningTheRestOfTheRule()
    {
        // The names widening must not widen the dates or the place with them:
        // Ana or Ben, but only in Genting, and only that March.
        int ana = AddPerson("Ana");
        int ben = AddPerson("Ben");
        int genting = AddPlace("Genting");
        int ipoh = AddPlace("Ipoh");

        int anaThere = Add("ana-genting.jpg", March, placeId: genting);
        Name(anaThere, ana);

        int benThere = Add("ben-genting.jpg", March, placeId: genting);
        Name(benThere, ben);

        int benElsewhere = Add("ben-ipoh.jpg", March, placeId: ipoh);
        Name(benElsewhere, ben);

        int anaWrongYear = Add("ana-later.jpg", March.AddYears(1), placeId: genting);
        Name(anaWrongYear, ana);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March.AddDays(-1)),
            DateOnly.FromDateTime(March.AddDays(1)),
            [ana, ben],
            [genting]));

        IReadOnlyList<int> fitting = await Repository().SuggestAsync(album);

        Assert.Equal([anaThere, benThere], fitting.Order());
        Assert.DoesNotContain(benElsewhere, fitting);
        Assert.DoesNotContain(anaWrongYear, fitting);
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

        int album = await Rule(new AlbumRule(null, null, [], [genting, ipoh]));

        Assert.Equal([one, two], (await Repository().SuggestAsync(album)).Order());
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

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March.AddDays(-1)),
            DateOnly.FromDateTime(March.AddDays(1)),
            [ana],
            [genting]));

        Assert.Equal([wanted], await Repository().SuggestAsync(album));
        Assert.DoesNotContain(noAna, await Repository().SuggestAsync(album));
    }

    [Fact]
    public async Task AProposedFaceIsNotEnoughToCount()
    {
        // A proposal is a question the user has not answered. Using it to fill
        // an album would answer it for them.
        int ana = AddPerson("Ana");
        int guessed = Add("guessed.jpg", March);
        Name(guessed, ana, AssignmentSource.Proposed);

        int album = await Rule(new AlbumRule(null, null, [ana], []));

        Assert.Empty(await Repository().SuggestAsync(album));
    }

    /// <summary>
    /// An album somebody made holds its photographs against every other album.
    /// </summary>
    /// <summary>
    /// A video fits a day rule, which until now it never could.
    /// </summary>
    /// <remarks>
    /// Not one of the 6,357 videos in the real library carries a capture date,
    /// so a rule naming the day of an outing found its photographs and silently
    /// left every clip behind - while the gallery, which files a picture under
    /// the same fallback, showed those clips sitting on that very day.
    /// </remarks>
    [Fact]
    public async Task AVideoWithNoCaptureDateFitsADayRule()
    {
        int clip = Add("clip.mp4", null, kind: AssetKind.Video, fileDate: March);
        Add("later.mp4", null, kind: AssetKind.Video, fileDate: March.AddDays(3));

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([clip], await Repository().SuggestAsync(album));
    }

    /// <summary>
    /// And the sentinel creation date does not file a row under year one.
    /// </summary>
    /// <remarks>
    /// The one part of the fallback that can behave differently in the database
    /// than in memory: rows indexed before creation dates were recorded hold
    /// DateTime.MinValue, and SQLite compares these as text. Reading that as
    /// "the earlier of the two" would date every such row to 0001-01-01, which
    /// no rule a person types would ever match.
    /// </remarks>
    [Fact]
    public async Task ASentinelCreationDateDoesNotFileAFileUnderYearOne()
    {
        int old = Add(
            "old.mp4",
            null,
            kind: AssetKind.Video,
            fileDate: March,
            createdUtc: default(DateTime));

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([old], await Repository().SuggestAsync(album));
    }

    [Fact]
    public async Task WhatIsAlreadySomewhereElseIsNotOffered()
    {
        int taken = Add("taken.jpg", March);
        int free = Add("free.jpg", March);

        IAlbumRepository repository = Repository();
        int somewhereElse = await repository.CreateAsync("Somewhere else");
        await repository.AddAsync(somewhereElse, [taken]);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([free], await Repository().SuggestAsync(album));
    }

    /// <summary>
    /// A suggestion holds nothing back, because it is a question and not a claim.
    /// </summary>
    /// <remarks>
    /// The clusterer sweeps every day carrying enough photographs into a
    /// proposal, which on a real library is half of everything in it - so a
    /// rule naming a day the app has already grouped could never find a single
    /// photograph, and a day worth photographing is exactly the day somebody is
    /// most likely to type. The same line is drawn in the clusterer's own feed,
    /// which re-groups whatever a proposal holds and leaves alone only what a
    /// person has spoken for.
    /// </remarks>
    [Fact]
    public async Task WhatASuggestionHoldsIsStillOffered()
    {
        int suggested = Add("suggested.jpg", March);
        Proposal("Eight days in March", suggested);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([suggested], await Repository().SuggestAsync(album));
    }

    /// <summary>
    /// A suggestion somebody kept holds them back, because they kept it.
    /// </summary>
    [Fact]
    public async Task WhatAKeptSuggestionHoldsIsNotOffered()
    {
        int kept = Add("kept.jpg", March);
        int free = Add("free.jpg", March);

        IAlbumRepository repository = Repository();
        int proposal = Proposal("Eight days in March", kept);
        await repository.AcceptAsync(proposal);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        Assert.Equal([free], await Repository().SuggestAsync(album));
    }

    /// <summary>
    /// Nothing is ever offered back to the album already holding it.
    /// </summary>
    /// <remarks>
    /// Worth its own test now that a photograph in a suggestion is offered at
    /// all: the album doing the asking may itself be a suggestion, and the rule
    /// that lets its photographs travel would otherwise let them travel back to
    /// where they already are.
    /// </remarks>
    [Fact]
    public async Task ASuggestionIsNeverOfferedItsOwnPhotographs()
    {
        int inside = Add("inside.jpg", March);
        int proposal = Proposal("Eight days in March", inside);

        await Repository().SetRuleAsync(proposal, new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));
        _db.ChangeTracker.Clear();

        Assert.Empty(await Repository().SuggestAsync(proposal));
    }

    /// <summary>
    /// Keeping one takes it out of the suggestion, and says which one it left.
    /// </summary>
    [Fact]
    public async Task KeepingOneTakesItOutOfTheSuggestion()
    {
        int moving = Add("moving.jpg", March);
        int staying = Add("staying.jpg", March);
        int proposal = Proposal("Eight days in March", moving, staying);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        AlbumAddResult result = await Repository().AddAsync(album, [moving]);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Moved);
        Assert.Equal(["Eight days in March"], result.From);
        Assert.Equal([moving], await Repository().GetMembersAsync(album));
        Assert.Equal([staying], await Repository().GetMembersAsync(proposal));
    }

    /// <summary>
    /// A suggestion the move emptied goes; one still holding something stays.
    /// </summary>
    /// <remarks>
    /// Nothing would ever arrive to fill the empty one: the clusterer's feed
    /// skips photographs a made album has spoken for, so the days it was built
    /// from are not offered again. Left on the wall it would be a suggested
    /// album holding no photographs, which is not a question anybody can answer.
    /// </remarks>
    [Fact]
    public async Task ASuggestionTheMoveEmptiedGoes()
    {
        int one = Add("one.jpg", March);
        int two = Add("two.jpg", March);
        int three = Add("three.jpg", March);

        int emptied = Proposal("Emptied", one);
        int partly = Proposal("Partly emptied", two, three);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        await Repository().AddAsync(album, [one, two]);

        int[] left = [.. (await Repository().GetAsync()).Select(row => row.Id)];

        Assert.DoesNotContain(emptied, left);
        Assert.Contains(partly, left);
        Assert.Equal([three], await Repository().GetMembersAsync(partly));
    }

    /// <summary>
    /// Refusing one records the refusal and moves the photograph nowhere.
    /// </summary>
    /// <remarks>
    /// Both answer paths used to write a refusal by adding the photograph and
    /// taking it straight back out, which was invisible only while a photograph
    /// already in an album could never be offered. Now that it can, that round
    /// trip would answer a question about this album by emptying a different
    /// one, and leave the photograph in no album at all.
    /// </remarks>
    [Fact]
    public async Task RefusingOneLeavesItWhereItIs()
    {
        int refused = Add("refused.jpg", March);
        int proposal = Proposal("Eight days in March", refused);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        await Repository().RefuseAsync(album, [refused]);

        Assert.Equal([refused], await Repository().GetMembersAsync(proposal));
        Assert.Empty(await Repository().GetMembersAsync(album));
        Assert.Empty(await Repository().SuggestAsync(album));
    }

    /// <summary>
    /// An album has a cover as soon as its first photographs arrive.
    /// </summary>
    /// <remarks>
    /// A cover is chosen by reading the memberships back out of the database,
    /// so they have to be written before it is chosen. They were not, and the
    /// album whose photographs all arrived in one press was left with none -
    /// hidden until now by the refusal path, which added photographs and took
    /// them out again, and chose a cover on the way past.
    /// </remarks>
    [Fact]
    public async Task ACoverIsChosenWhenTheFirstPhotographsArrive()
    {
        int one = Add("one.jpg", March);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        await Repository().AddAsync(album, [one]);

        AlbumSummary made = (await Repository().GetAsync()).Single(row => row.Id == album);

        Assert.Equal("one.jpg", made.CoverThumbnailName);
    }

    [Fact]
    public async Task WhatWasTakenOutIsNotOfferedBack()
    {
        // Otherwise the button would hand back exactly what the user had just
        // rejected, every time they pressed it.
        int one = Add("one.jpg", March);
        int two = Add("two.jpg", March);

        int album = await Rule(new AlbumRule(
            DateOnly.FromDateTime(March), DateOnly.FromDateTime(March), [], []));

        IAlbumRepository repository = Repository();
        await repository.AddAsync(album, [one, two]);
        await repository.RemoveAsync(album, [two]);

        Assert.Empty(await Repository().SuggestAsync(album));

        // And it stays out after it has been taken out of the album too.
        await repository.RemoveAsync(album, [one]);
        Assert.Empty(await Repository().SuggestAsync(album));
    }

    [Fact]
    public async Task ARuleIsReadBackAsItWasWritten()
    {
        int ana = AddPerson("Ana");
        int genting = AddPlace("Genting");
        var written = new AlbumRule(
            new DateOnly(2019, 3, 1), new DateOnly(2019, 3, 31), [ana], [genting]);

        int album = await Rule(written);

        Assert.Equal(written, await Repository().GetRuleAsync(album));
    }

    [Fact]
    public async Task SettingARuleReplacesTheOneBefore()
    {
        int ana = AddPerson("Ana");
        int ben = AddPerson("Ben");

        int album = await Rule(new AlbumRule(null, null, [ana, ben], []));
        await Repository().SetRuleAsync(album, new AlbumRule(null, null, [ben], []));

        AlbumRule now = await Repository().GetRuleAsync(album);

        Assert.Equal([ben], now.PersonIds);
    }

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private int _nextId;

    public AlbumRuleTests()
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

    private IAlbumRepository Repository() => new SqliteAlbumRepository(_db);

    /// <summary>An album of the user's own, looking for this.</summary>
    private async Task<int> Rule(AlbumRule rule)
    {
        IAlbumRepository repository = Repository();
        int album = await repository.CreateAsync("Looking for");
        await repository.SetRuleAsync(album, rule);
        _db.ChangeTracker.Clear();

        return album;
    }

    /// <summary>A suggestion the app made, holding these photographs.</summary>
    /// <remarks>
    /// Written as a row rather than through SaveProposalsAsync, because all
    /// these tests need of a proposal is what it is and what it holds; going
    /// through the pass would drag the clusterer in to say both.
    /// </remarks>
    private int Proposal(string name, params int[] assetIds)
    {
        var album = new Album
        {
            Name = name,
            StartUtc = March,
            EndUtc = March,
            Kind = AlbumKind.Period,
            Origin = AlbumOrigin.Proposed,
            ProposalKey = $"days:{name}",
            BuiltUtc = March,
        };

        foreach (int assetId in assetIds)
        {
            album.Members.Add(new AlbumMember { AssetId = assetId, AddedUtc = March });
        }

        _db.Albums.Add(album);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return album.Id;
    }

    /// <summary>
    /// A photograph in the library, with or without a date of its own.
    /// </summary>
    /// <param name="fileDate">
    /// What the file's own timestamps say, for the rows that carry no capture
    /// date - which is every video in a real library.
    /// </param>
    /// <param name="createdUtc">
    /// The creation date on its own, where a test needs it to differ from the
    /// modified date: the sentinel, or a date a copy pushed later.
    /// </param>
    private int Add(
        string relativePath,
        DateTime? takenUtc,
        int? placeId = null,
        AssetKind kind = AssetKind.Photo,
        DateTime? fileDate = null,
        DateTime? createdUtc = null)
    {
        DateTime stamp = fileDate ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1024,
            ModifiedUtc = stamp,
            CreatedUtc = createdUtc ?? stamp,
            IndexedUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TakenUtc = takenUtc,
            PlaceId = placeId,
            Kind = kind,
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
