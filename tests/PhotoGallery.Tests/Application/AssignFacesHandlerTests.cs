using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

public sealed class AssignFacesHandlerTests : IDisposable
{
    private static readonly DateTime s_start = new(2014, 3, 11, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqlitePeopleReader _reader;
    private readonly SqlitePeopleRepository _repository;

    private int _nextAsset = 1;

    public AssignFacesHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-people-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();

        _reader = new SqlitePeopleReader(_db);
        _repository = new SqlitePeopleRepository(_db);

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _tempRoot });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Name_CreatesThePersonAndConfirmsTheirFaces()
    {
        int[] faces = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];

        AssignmentResult result = await Name(faces, "Ana Lim");

        Assert.Equal("Ana Lim", result.DisplayName);
        Assert.Equal(3, result.Assigned);
        Assert.Equal(
            3,
            await _db.FaceAssignments.CountAsync(a =>
                a.PersonId == result.PersonId && a.Source == AssignmentSource.Confirmed));
    }

    [Fact]
    public async Task Name_TheSameNameASecondTimeIsTheSamePerson()
    {
        // The whole reason a second group can be named at all: two stretches of
        // one childhood are one person with two eras, not two people.
        AssignmentResult first = await Name([AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)], "Ana Lim");
        AssignmentResult second =
            await Name([AddFace(900, 70), AddFace(901, 72), AddFace(902, 74)], "Ana Lim");

        Assert.Equal(first.PersonId, second.PersonId);
        Assert.Equal(1, await _db.Set<Person>().CountAsync());

        // Both stretches now belong to one person. How that person's appearance
        // is divided into eras is EraBuilder's business and is tested there.
        Assert.Equal(
            6,
            await _db.FaceAssignments.CountAsync(a =>
                a.PersonId == first.PersonId && a.Source == AssignmentSource.Confirmed));
    }

    [Fact]
    public async Task Name_ProposesTheOtherFacesThatLookLikeThem()
    {
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        int looksAlike = AddFace(3, 6);
        int stranger = AddFace(4, 120);

        AssignmentResult result = await Name(named, "Ana Lim");

        Assert.Equal(1, result.Proposed);
        Assert.Equal(
            AssignmentSource.Proposed,
            (await _db.FaceAssignments.FirstAsync(a => a.FaceId == looksAlike)).Source);
        Assert.False(await _db.FaceAssignments.AnyAsync(a => a.FaceId == stranger));
    }

    [Fact]
    public async Task Confirm_TurnsAProposalIntoSomethingTheErasAreBuiltFrom()
    {
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        int proposed = AddFace(3, 6);
        AssignmentResult first = await Name(named, "Ana Lim");

        AssignmentResult confirmed = await NewHandler().HandleAsync(new AssignFacesRequest(
            [proposed], AssignmentSource.Confirmed, PersonId: first.PersonId));

        Assert.Equal(
            4,
            await _db.FaceAssignments.CountAsync(a =>
                a.PersonId == first.PersonId && a.Source == AssignmentSource.Confirmed));
        Assert.Equal(4, (await _db.Set<PersonEra>().FirstAsync()).SampleCount);
        Assert.Equal(0, confirmed.Proposed);
    }

    [Fact]
    public async Task Reject_LeavesTheFaceUnnamedAndNeverOffersItAgain()
    {
        // Rejections are kept rather than forgotten. Forgetting one would have
        // the very next round make exactly the same wrong suggestion.
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        int wrong = AddFace(3, 6);
        AssignmentResult first = await Name(named, "Ana Lim");
        Assert.Equal(1, first.Proposed);

        await NewHandler().HandleAsync(new AssignFacesRequest(
            [wrong], AssignmentSource.Rejected, PersonId: first.PersonId));

        AssignmentResult again = await NewHandler().HandleAsync(new AssignFacesRequest(
            [AddFace(4, 1)], AssignmentSource.Confirmed, PersonId: first.PersonId));

        Assert.Equal(0, again.Proposed);
        Assert.Equal(
            AssignmentSource.Rejected,
            (await _db.FaceAssignments.FirstAsync(a => a.FaceId == wrong)).Source);
    }

    [Fact]
    public async Task Name_DoesNotOfferFacesThatAlreadyBelongToSomeoneElse()
    {
        int[] hers = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        await Name(hers, "Mum");

        AssignmentResult his = await Name([AddFace(3, 3), AddFace(4, 5), AddFace(5, 7)], "Dad");

        Assert.Equal(0, his.Proposed);
    }

    [Fact]
    public async Task Name_BuildsErasOnlyFromWhatWasConfirmed()
    {
        // A proposal teaching the app what it already believes would make every
        // later proposal worse, and a wrong one would pull the era off the
        // person entirely.
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        AddFace(3, 6);

        AssignmentResult result = await Name(named, "Ana Lim");

        Assert.Equal(1, result.Eras);
        Assert.Equal(3, (await _db.Set<PersonEra>().FirstAsync()).SampleCount);
    }

    [Fact]
    public async Task Name_SaysWhenItIsShowingOnlySomeOfWhatItFound()
    {
        // Three people all reporting exactly three hundred is a cap being met.
        // A number that stops at a round figure without saying so reads as the
        // whole answer.
        int[] named = [AddFace(0, 0), AddFace(1, 1), AddFace(2, 2)];
        for (int i = 0; i < ProposeFacesHandler.MaxProposals + 20; i++)
        {
            AddFace(3 + (i % 60), 3);
        }

        AssignmentResult result = await Name(named, "Ana Lim");

        Assert.True(result.WasCapped);
        Assert.Equal(ProposeFacesHandler.MaxProposals, result.Proposed);
        Assert.True(result.Matched > result.Proposed);
        Assert.Contains("showing the closest", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ignore_StopsAFaceBeingOfferedToAnybody()
    {
        // A rejection says a face is not one person, which leaves it to be
        // offered as everyone else in turn. Setting it aside says it is nobody.
        int stranger = AddFace(3, 6);
        await _repository.SetIgnoredAsync([stranger], ignored: true);

        AssignmentResult result = await Name([AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)], "Ana Lim");

        Assert.Equal(0, result.Proposed);
        Assert.False(await _db.FaceAssignments.AnyAsync(a => a.FaceId == stranger));
    }

    [Fact]
    public async Task Ignore_IsUndoneBySayingWhoTheyAre()
    {
        // A face set aside by mistake is one name away from being wanted again.
        int face = AddFace(0, 0);
        await _repository.SetIgnoredAsync([face], ignored: true);
        await _repository.SetIgnoredAsync([face], ignored: false);

        AssignmentResult result = await Name([face], "Ana Lim");

        Assert.Equal(1, result.Assigned);
        Assert.Null((await _db.Faces.AsNoTracking().FirstAsync(f => f.Id == face)).IgnoredUtc);
    }

    [Fact]
    public async Task Ignore_ThrowsAwayWhatWasSaidAboutTheFace()
    {
        // Whatever was proposed about a face that turns out to be nobody was
        // said about the wrong thing, so it goes with it.
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        int proposed = AddFace(3, 6);
        await Name(named, "Ana Lim");
        Assert.True(await _db.FaceAssignments.AnyAsync(a => a.FaceId == proposed));

        await _repository.SetIgnoredAsync([proposed], ignored: true);

        Assert.False(await _db.FaceAssignments.AnyAsync(a => a.FaceId == proposed));
    }

    [Fact]
    public async Task Remove_ReleasesEveryFaceThatWasNamedAsThem()
    {
        // Removing a name must not remove the faces. They go back to being
        // unnamed, so the pictures are all still there to be named again -
        // which is what makes removing safe after a mistake.
        int[] faces = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        AssignmentResult named = await Name(faces, "Ana Lim");

        await _repository.RemovePersonAsync(named.PersonId);

        Assert.Equal(0, await _db.Set<Person>().CountAsync());
        Assert.Equal(0, await _db.FaceAssignments.CountAsync());
        Assert.Equal(0, await _db.Set<PersonEra>().CountAsync());

        // The faces themselves are untouched.
        Assert.Equal(faces.Length, await _db.Faces.CountAsync());
        Assert.All(
            await _db.Faces.AsNoTracking().ToListAsync(),
            face => Assert.Null(face.IgnoredUtc));
    }

    [Fact]
    public async Task Remove_LetsTheSameFacesBeNamedAgainAfterwards()
    {
        int[] faces = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        AssignmentResult first = await Name(faces, "Ana Lim");
        await _repository.RemovePersonAsync(first.PersonId);

        AssignmentResult again = await Name(faces, "Ana Lim");

        Assert.Equal(3, again.Assigned);
        Assert.NotEqual(first.PersonId, again.PersonId);
    }

    [Fact]
    public async Task Name_DoesNotOfferFacesTheDetectorWasBarelySureOf()
    {
        // The bottom of the detector's range is where it puts boxes on things
        // that are not faces. Measured on the real library: nothing the user has
        // ever confirmed scored below 0.62, so offering that range only spends
        // their attention on the detector's mistakes.
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        int barely = AddFace(3, 6, score: 0.55f);
        int sure = AddFace(4, 6, score: 0.85f);

        await Name(named, "Ana Lim");

        Assert.False(await _db.FaceAssignments.AnyAsync(a => a.FaceId == barely));
        Assert.True(await _db.FaceAssignments.AnyAsync(a => a.FaceId == sure));
    }

    [Fact]
    public async Task Handle_RefusesAnEmptyList()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => NewHandler().HandleAsync(
                new AssignFacesRequest([], AssignmentSource.Confirmed, "Nobody")));
    }

    [Fact]
    public async Task Name_OffersAFaceToWhoeverItLooksMostLikeRatherThanWhoeverAsksFirst()
    {
        // The sibling problem, and the reason routing exists. Anna comes first
        // alphabetically and the face clears her threshold comfortably - under
        // the old rule, which asked each person separately and let the first one
        // over the line take it, she got it. It is Ben's, by a mile.
        await Name([AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)], "Anna");
        AssignmentResult ben = await Name([AddFace(3, 50), AddFace(4, 52), AddFace(5, 54)], "Ben");

        int contested = AddFace(6, 45);
        await NewHandler().RefreshAsync(ben.PersonId);

        FaceAssignment claim = await _db.FaceAssignments
            .AsNoTracking().FirstAsync(a => a.FaceId == contested);

        Assert.Equal(ben.PersonId, claim.PersonId);
        Assert.Equal(AssignmentSource.Proposed, claim.Source);
    }

    [Fact]
    public async Task Name_AsksNobodyWhenTwoPeopleAreTooCloseToSeparate()
    {
        // Halfway between two people is not a weak match for either - it is a
        // question the app cannot answer, and guessing at it teaches the wrong
        // person's era from the user's confirmation.
        await Name([AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)], "Anna");
        AssignmentResult ben = await Name([AddFace(3, 50), AddFace(4, 52), AddFace(5, 54)], "Ben");

        int between = AddFace(6, 27);
        await NewHandler().RefreshAsync(ben.PersonId);

        Assert.False(await _db.FaceAssignments.AnyAsync(a => a.FaceId == between));
    }

    [Fact]
    public async Task Reject_LetsTheFaceFallToWhoeverWasSecond()
    {
        // "Not Anna" is not "nobody". Refusing somebody takes them out of the
        // running for that face, which is what lets the next best person be
        // offered it instead of the face going quiet.
        AssignmentResult anna = await Name([AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)], "Anna");
        AssignmentResult ben = await Name([AddFace(3, 50), AddFace(4, 52), AddFace(5, 54)], "Ben");

        int face = AddFace(6, 5);
        await NewHandler().RefreshAsync(anna.PersonId);
        Assert.Equal(
            anna.PersonId,
            (await _db.FaceAssignments.AsNoTracking().FirstAsync(a => a.FaceId == face)).PersonId);

        await NewHandler().HandleAsync(new AssignFacesRequest(
            [face], AssignmentSource.Rejected, PersonId: anna.PersonId));

        Assert.Equal(
            ben.PersonId,
            (await _db.FaceAssignments.AsNoTracking()
                .FirstAsync(a => a.FaceId == face && a.Source == AssignmentSource.Proposed))
                .PersonId);

        // And Anna's refusal survives being offered to Ben. Writing one person's
        // answer used to clear every row for the face, which threw away the
        // promise not to ask again - so the very next round asked again.
        Assert.True(await _db.FaceAssignments.AnyAsync(a =>
            a.FaceId == face
            && a.PersonId == anna.PersonId
            && a.Source == AssignmentSource.Rejected));
    }

    private AssignFacesHandler NewHandler() =>
        new(_reader, _repository, new ProposeFacesHandler(_reader, _repository));

    [Fact]
    public async Task Name_SettlesEveryCopyOfThePhotographItWasGivenOn()
    {
        // The queue asks about a face once however many files the photograph
        // exists as, and the badge counts it once. Naming only the row that
        // happened to stand for the group left the others unnamed, so the next
        // round put one of them up in its place - the same crop, the same count,
        // and the answer apparently ignored.
        const string OnePhotograph = "thumb-two-files.jpg";
        int asked = AddFace(0, 0, thumbnailName: OnePhotograph);
        int otherCopy = AddFace(0, 0, thumbnailName: OnePhotograph);
        int anotherPicture = AddFace(9, 0);

        AssignmentResult result = await Name([asked], "Ana");

        int[] named = await Confirmed(result.PersonId);

        Assert.Equal([asked, otherCopy], named);
        Assert.DoesNotContain(anotherPicture, named);

        // One question was answered, whatever that came to in rows.
        Assert.Equal(1, result.Assigned);
    }

    [Fact]
    public async Task Name_DoesNotTakeACopyAlreadyConfirmedAsSomebodyElse()
    {
        // Carrying an answer across the copies of one photograph must not reach
        // into another person's: a face confirmed as somebody else is a decision
        // the user made, and naming this one is not a place to overturn it.
        const string OnePhotograph = "thumb-two-answers.jpg";
        int hers = AddFace(0, 0, thumbnailName: OnePhotograph);
        int asked = AddFace(0, 0, thumbnailName: OnePhotograph);

        int bea = (await Name([hers], "Bea")).PersonId;

        AssignmentResult result = await Name([asked], "Ana");

        Assert.Contains(
            hers,
            await Confirmed(bea));

        // Ana got the face she was asked about and nothing of Bea's.
        Assert.Equal([asked], await Confirmed(result.PersonId));
    }

    /// <summary>The faces confirmed as one person, in order.</summary>
    private async Task<int[]> Confirmed(int personId) =>
        await _db.FaceAssignments
            .Where(a => a.PersonId == personId && a.Source == AssignmentSource.Confirmed)
            .Select(a => a.FaceId)
            .OrderBy(id => id)
            .ToArrayAsync();

    private Task<AssignmentResult> Name(IReadOnlyList<int> faceIds, string name) =>
        NewHandler().HandleAsync(
            new AssignFacesRequest(faceIds, AssignmentSource.Confirmed, DisplayName: name));

    /// <summary>Adds a photo with one face in it, and returns the face's id.</summary>
    /// <param name="thumbnailName">
    /// Shared to make a second file of the same photograph. Renditions are named
    /// after the picture's content, so two files carrying one name are two copies
    /// of one photograph - which is what the queue collapses.
    /// </param>
    private int AddFace(
        int dayOffset, double angle, float score = 0.9f, string? thumbnailName = null)
    {
        int assetId = _nextAsset++;
        _db.Assets.Add(new Asset
        {
            Id = assetId,
            PhotoSourceId = 1,
            RelativePath = $"20200214 - Event\\photo-{assetId}.jpg",
            Length = 1,
            ModifiedUtc = s_start,
            CreatedUtc = s_start,
            IndexedUtc = s_start,
            TakenUtc = s_start.AddDays(dayOffset),
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = thumbnailName ?? $"thumb{assetId:D4}.jpg",
        });

        var face = new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(0, 0, 80, 80),
            DetectScore = score,
            Embedding = TestEmbeddings.At(angle),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        return face.Id;
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
