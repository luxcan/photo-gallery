using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
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
        ProposalRound round = await Round();

        Assert.Equal(1, round.For(result.PersonId).Offered);
        Assert.Equal(
            AssignmentSource.Proposed,
            (await _db.FaceAssignments.FirstAsync(a => a.FaceId == looksAlike)).Source);
        Assert.False(await _db.FaceAssignments.AnyAsync(a => a.FaceId == stranger));
    }

    [Fact]
    public async Task Name_SavesTheAnswerWithoutRunningARound()
    {
        // The round is a sweep over the whole library, and running it inside
        // every answer made naming ten faces in a row ten waits. The answer and the person's eras are saved
        // here; who else looks like them is the background round's to decide.
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        int looksAlike = AddFace(3, 6);

        AssignmentResult result = await Name(named, "Ana Lim");

        Assert.Equal(1, result.Eras);
        Assert.False(await _db.FaceAssignments.AnyAsync(a => a.FaceId == looksAlike));
        Assert.Equal("3 faces are Ana Lim", result.Summary);
    }

    [Fact]
    public async Task Round_NeverWritesOverAnAnswerGivenWhileItRan()
    {
        // The round reads the library, spends seconds weighing it, then writes.
        // Answers keep arriving in that time now that it runs in the background,
        // and what it chose from its first read must not land on a face the user
        // has since confirmed, refused for that person, or set aside.
        AssignmentResult ana = await Name([AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)], "Ana Lim");
        int confirmed = AddFace(3, 5);
        int refused = AddFace(4, 6);
        int setAside = AddFace(5, 4);
        int untouched = AddFace(6, 5);

        var held = new HeldAtTheWrite(_repository);
        Task<ProposalRound> running = new ProposeFacesHandler(_reader, held).HandleAsync();
        await held.Reached.WaitAsync(TimeSpan.FromSeconds(10));

        // Answered on a connection of its own, as the screen's write would be.
        await using (GalleryDbContext other = NewContext())
        {
            var reader = new SqlitePeopleReader(other);
            var repository = new SqlitePeopleRepository(other);
            var answer = new AssignFacesHandler(reader, repository);

            await answer.HandleAsync(new AssignFacesRequest(
                [confirmed], AssignmentSource.Confirmed, PersonId: ana.PersonId));
            await answer.HandleAsync(new AssignFacesRequest(
                [refused], AssignmentSource.Rejected, PersonId: ana.PersonId));
            await repository.SetIgnoredAsync([setAside], ignored: true);
        }

        held.Release();
        ProposalRound round = await running;

        // The round had chosen all four from what it read.
        Assert.Equal(4, round.For(ana.PersonId).Offered);

        Assert.False(await _db.FaceAssignments.AnyAsync(a =>
            a.Source == AssignmentSource.Proposed
            && (a.FaceId == confirmed || a.FaceId == refused || a.FaceId == setAside)));
        Assert.True(await _db.FaceAssignments.AnyAsync(a =>
            a.FaceId == confirmed && a.Source == AssignmentSource.Confirmed));
        Assert.True(await _db.FaceAssignments.AnyAsync(a =>
            a.FaceId == refused && a.Source == AssignmentSource.Rejected));
        Assert.True(await _db.FaceAssignments.AnyAsync(a =>
            a.FaceId == untouched && a.Source == AssignmentSource.Proposed));
    }

    [Fact]
    public async Task Round_KeepsTheLastRoundsQuestionsUntilItWritesItsOwn()
    {
        // A round no longer clears every proposal before it reads. Something
        // reading the library while it works - the People screen putting its
        // queue back after an answer - found no questions at all and closed the
        // queue under the user.
        AssignmentResult ana = await Name([AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)], "Ana Lim");
        int proposed = AddFace(3, 5);
        await Round();

        var held = new HeldAtTheWrite(_repository);
        Task<ProposalRound> running = new ProposeFacesHandler(_reader, held).HandleAsync();
        await held.Reached.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(await _db.FaceAssignments.AnyAsync(a =>
            a.FaceId == proposed && a.PersonId == ana.PersonId && a.Source == AssignmentSource.Proposed));

        held.Release();
        await running;

        Assert.Equal(
            1,
            await _db.FaceAssignments.CountAsync(a =>
                a.FaceId == proposed && a.Source == AssignmentSource.Proposed));
    }

    [Fact]
    public async Task Confirm_TurnsAProposalIntoSomethingTheErasAreBuiltFrom()
    {
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        int proposed = AddFace(3, 6);
        AssignmentResult first = await Name(named, "Ana Lim");

        await Round();

        await NewHandler().HandleAsync(new AssignFacesRequest(
            [proposed], AssignmentSource.Confirmed, PersonId: first.PersonId));
        ProposalRound round = await Round();

        Assert.Equal(
            4,
            await _db.FaceAssignments.CountAsync(a =>
                a.PersonId == first.PersonId && a.Source == AssignmentSource.Confirmed));
        Assert.Equal(4, (await _db.Set<PersonEra>().FirstAsync()).SampleCount);
        Assert.Equal(0, round.For(first.PersonId).Offered);
    }

    [Fact]
    public async Task Reject_LeavesTheFaceUnnamedAndNeverOffersItAgain()
    {
        // Rejections are kept rather than forgotten. Forgetting one would have
        // the very next round make exactly the same wrong suggestion.
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        int wrong = AddFace(3, 6);
        AssignmentResult first = await Name(named, "Ana Lim");
        Assert.Equal(1, (await Round()).For(first.PersonId).Offered);

        await NewHandler().HandleAsync(new AssignFacesRequest(
            [wrong], AssignmentSource.Rejected, PersonId: first.PersonId));

        await NewHandler().HandleAsync(new AssignFacesRequest(
            [AddFace(4, 1)], AssignmentSource.Confirmed, PersonId: first.PersonId));

        Assert.Equal(0, (await Round()).For(first.PersonId).Offered);
        Assert.Equal(
            AssignmentSource.Rejected,
            (await _db.FaceAssignments.FirstAsync(a => a.FaceId == wrong)).Source);
    }

    [Fact]
    public async Task Reject_SaysTheFacesAreNotThem()
    {
        // It used to say "4 faces are King Loong" straight after the user had
        // said they were not - the sentence never knew which answer was given.
        int[] named = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        AssignmentResult ana = await Name(named, "Ana Lim");

        AssignmentResult one = await NewHandler().HandleAsync(new AssignFacesRequest(
            [AddFace(3, 6)], AssignmentSource.Rejected, PersonId: ana.PersonId));
        AssignmentResult two = await NewHandler().HandleAsync(new AssignFacesRequest(
            [AddFace(4, 7), AddFace(5, 8)], AssignmentSource.Rejected, PersonId: ana.PersonId));

        Assert.StartsWith("1 face is not Ana Lim", one.Summary, StringComparison.Ordinal);
        Assert.StartsWith("2 faces are not Ana Lim", two.Summary, StringComparison.Ordinal);
        Assert.StartsWith("3 faces are Ana Lim", ana.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Name_DoesNotOfferFacesThatAlreadyBelongToSomeoneElse()
    {
        int[] hers = [AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)];
        await Name(hers, "Mum");

        AssignmentResult his = await Name([AddFace(3, 3), AddFace(4, 5), AddFace(5, 7)], "Dad");

        Assert.Equal(0, (await Round()).For(his.PersonId).Offered);
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
        ProposalRound round = await Round();
        PersonProposals theirs = round.For(result.PersonId);

        Assert.True(round.WasCapped);
        Assert.Equal(ProposeFacesHandler.MaxProposals, theirs.Offered);
        Assert.True(theirs.Matched > theirs.Offered);
        Assert.Contains("showing the closest", round.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ignore_StopsAFaceBeingOfferedToAnybody()
    {
        // A rejection says a face is not one person, which leaves it to be
        // offered as everyone else in turn. Setting it aside says it is nobody.
        int stranger = AddFace(3, 6);
        await _repository.SetIgnoredAsync([stranger], ignored: true);

        AssignmentResult result = await Name([AddFace(0, 0), AddFace(1, 2), AddFace(2, 4)], "Ana Lim");

        Assert.Equal(0, (await Round()).For(result.PersonId).Offered);
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
        await Round();
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
    public async Task Remove_LeavesNeitherTheirNameNorTheirBirthYearOnWhatIsLeft()
    {
        // The row stays so the deletion can travel, and the row is all that
        // stays: a name kept on it went into the sync folder on every sync.
        // Two of them, because every tombstone now has the same empty name.
        AssignmentResult ana = await Name([AddFace(0, 0)], "Ana Lim");
        AssignmentResult ben = await Name([AddFace(1, 2)], "Ben Lim");
        await _db.Set<Person>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.BirthYear, 2015));

        await _repository.RemovePersonAsync(ana.PersonId);
        await _repository.RemovePersonAsync(ben.PersonId);

        List<Person> left = await _db.Set<Person>().IgnoreQueryFilters().AsNoTracking().ToListAsync();

        Assert.Equal(2, left.Count);
        Assert.All(left, person =>
        {
            Assert.NotNull(person.DeletedUtc);
            Assert.Equal(string.Empty, person.DisplayName);
            Assert.Null(person.BirthYear);
        });
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
        await Round();

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
        await Round();

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
        await Round();

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
        await Round();
        Assert.Equal(
            anna.PersonId,
            (await _db.FaceAssignments.AsNoTracking().FirstAsync(a => a.FaceId == face)).PersonId);

        await NewHandler().HandleAsync(new AssignFacesRequest(
            [face], AssignmentSource.Rejected, PersonId: anna.PersonId));
        await Round();

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

    private AssignFacesHandler NewHandler() => new(_reader, _repository);

    /// <summary>One suggestion round, as the app runs after an answer is saved.</summary>
    private Task<ProposalRound> Round() =>
        new ProposeFacesHandler(_reader, _repository).HandleAsync();

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

        // Confirmed on that one file alone, as a copy that reached the library
        // after its twin was named would be. Naming Bea through the handler
        // would have carried her to the other copy too, and naming that copy
        // as Ana would then be correcting Bea rather than leaving her be.
        int bea = await ConfirmOnlyAsync(hers, "Bea");

        AssignmentResult result = await Name([asked], "Ana");

        Assert.Contains(
            hers,
            await Confirmed(bea));

        // Ana got the face she was asked about and nothing of Bea's.
        Assert.Equal([asked], await Confirmed(result.PersonId));
    }

    [Fact]
    public async Task Name_AFaceConfirmedAsSomebodyElse_MovesItAndRebuildsWhoItLeft()
    {
        // Naming a face that is already somebody is the user correcting it, and
        // the person it was taken from must stop learning from it: their eras
        // were built with this face among their examples.
        int moved = AddFace(0, 0);
        AssignmentResult anna = await Name([moved, AddFace(1, 2), AddFace(2, 4)], "Anna");
        Assert.Equal(3, await SamplesOf(anna.PersonId));

        AssignmentResult ben = await Name([moved], "Ben");

        Assert.Equal([moved], await Confirmed(ben.PersonId));
        Assert.DoesNotContain(moved, await Confirmed(anna.PersonId));
        Assert.Equal(2, await SamplesOf(anna.PersonId));
    }

    [Fact]
    public async Task Record_RebuildsWhoAFaceWasTakenFrom_ButNotWhoItNamed()
    {
        // Answering a queue one face at a time records without working out what
        // the answer means for the person named - that is left for the way out.
        // The person the face was taken from is another matter: only this
        // answer knows who they were.
        int moved = AddFace(0, 0);
        AssignmentResult anna = await Name([moved, AddFace(1, 2), AddFace(2, 4)], "Anna");
        Assert.Equal(3, await SamplesOf(anna.PersonId));

        int ben = await NewHandler().RecordAsync(
            new AssignFacesRequest([moved], AssignmentSource.Confirmed, DisplayName: "Ben"));

        Assert.Equal([moved], await Confirmed(ben));
        Assert.Equal(2, await SamplesOf(anna.PersonId));
        Assert.False(
            await _db.Set<PersonEra>().AnyAsync(era => era.PersonId == ben),
            "recording one answer rebuilt the person it named");
    }

    [Fact]
    public async Task Record_ReadsOnlyThePhotographsItWasAskedAbout()
    {
        // The queue records one answer per click and waits for it before moving
        // on, so finding the copies of one photograph must not cost a read of
        // every face in the library - the read the board is kept off the UI
        // thread for.
        const string OnePhotograph = "thumb-narrow.jpg";
        int asked = AddFace(0, 0, thumbnailName: OnePhotograph);
        int otherCopy = AddFace(0, 0, thumbnailName: OnePhotograph);
        int anotherPicture = AddFace(9, 0);

        var calls = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        IPeopleReader counted = CallCounter<IPeopleReader>.Wrap(_reader, calls);
        var handler = new AssignFacesHandler(counted, _repository);

        int ana = await handler.RecordAsync(
            new AssignFacesRequest([asked], AssignmentSource.Confirmed, DisplayName: "Ana"));

        Assert.False(
            calls.ContainsKey(nameof(IPeopleReader.GetFacesAsync)),
            "recording one answer read every face in the library");
        Assert.Equal([asked, otherCopy], await Confirmed(ana));
        Assert.DoesNotContain(anotherPicture, await Confirmed(ana));
    }

    [Fact]
    public async Task Name_AMovedFaceTakesItsCopiesWithIt()
    {
        // The copies were named together as one photograph; moving only the file
        // that happened to be open would show the same picture under two names.
        const string OnePhotograph = "thumb-moved.jpg";
        int asked = AddFace(0, 0, thumbnailName: OnePhotograph);
        int otherCopy = AddFace(0, 0, thumbnailName: OnePhotograph);

        AssignmentResult anna = await Name([asked], "Anna");
        Assert.Equal([asked, otherCopy], await Confirmed(anna.PersonId));

        AssignmentResult ben = await Name([asked], "Ben");

        Assert.Equal([asked, otherCopy], await Confirmed(ben.PersonId));
        Assert.Empty(await Confirmed(anna.PersonId));
    }

    [Fact]
    public async Task Name_AMovedFaceLeavesACopyConfirmedAsAThirdPerson()
    {
        // Only what was said about the face being corrected goes with it. A copy
        // somebody named as a third person is a separate decision.
        const string OnePhotograph = "thumb-three-names.jpg";
        int hers = AddFace(0, 0, thumbnailName: OnePhotograph);
        int asked = AddFace(0, 0, thumbnailName: OnePhotograph);

        int bea = await ConfirmOnlyAsync(hers, "Bea");
        await ConfirmOnlyAsync(asked, "Anna");

        AssignmentResult ben = await Name([asked], "Ben");

        Assert.Equal([asked], await Confirmed(ben.PersonId));
        Assert.Equal([hers], await Confirmed(bea));
    }

    [Fact]
    public async Task Name_ABatchCorrectsEachPhotographOnlyForWhoItsOwnFaceWas()
    {
        // A correction belongs to the photograph it was made on. Naming two faces
        // together, one of them a face of Anna's, must not reach into the other
        // photograph and take a copy somebody confirmed as Anna on its own.
        const string SecondPhotograph = "thumb-batch-second.jpg";
        int annasFace = AddFace(0, 0);
        int unclaimed = AddFace(1, 0, thumbnailName: SecondPhotograph);
        int independentCopy = AddFace(1, 0, thumbnailName: SecondPhotograph);

        int anna = await ConfirmOnlyAsync(annasFace, "Anna");
        await ConfirmOnlyAsync(independentCopy, "Anna");

        AssignmentResult ben = await Name([annasFace, unclaimed], "Ben");

        Assert.Equal([annasFace, unclaimed], await Confirmed(ben.PersonId));
        Assert.Equal([independentCopy], await Confirmed(anna));
    }

    [Fact]
    public async Task Name_AFaceConfirmedAsTwoPeople_RebuildsBothAndTakesBothTheirCopies()
    {
        // The state an older build left behind, and that another laptop may still
        // hold until it merges: one face confirmed as two people. Correcting it
        // takes it off both, so both must stop learning from it, and a copy
        // confirmed as either of them is the same photograph and goes with it.
        const string OnePhotograph = "thumb-two-people.jpg";
        int doubled = AddFace(0, 0, thumbnailName: OnePhotograph);
        int beasCopy = AddFace(0, 0, thumbnailName: OnePhotograph);

        // Bea's copy first, so naming Anna leaves it be rather than carrying to it.
        int bea = await ConfirmOnlyAsync(beasCopy, "Bea");
        int anna = (await Name([doubled, AddFace(1, 2), AddFace(2, 4)], "Anna")).PersonId;
        await ConfirmAlongsideAsync(doubled, bea);
        Assert.Equal(3, await SamplesOf(anna));
        Assert.Equal(2, await SamplesOf(bea));

        AssignmentResult ben = await Name([doubled], "Ben");

        Assert.Equal([doubled, beasCopy], await Confirmed(ben.PersonId));
        Assert.Equal(2, await SamplesOf(anna));
        Assert.Equal(0, await SamplesOf(bea));
    }

    [Fact]
    public async Task Name_AFaceConfirmedAsTwoPeople_AsOneOfThem_RebuildsTheOther()
    {
        // Naming the face as one of its two people settles it on that one, and
        // the other loses it whichever of the two the screens happened to show.
        int doubled = AddFace(0, 0);

        int anna = (await Name([doubled, AddFace(1, 2)], "Anna")).PersonId;
        int bea = (await Name([AddFace(2, 4)], "Bea")).PersonId;
        await ConfirmAlongsideAsync(doubled, bea);
        Assert.Equal(2, await SamplesOf(bea));

        await Name([doubled], "Anna");

        Assert.Contains(doubled, await Confirmed(anna));
        Assert.DoesNotContain(doubled, await Confirmed(bea));
        Assert.Equal(1, await SamplesOf(bea));
    }

    /// <summary>
    /// Adds a second confirmation for a face without taking it off whoever it is
    /// already confirmed as - what an older build did - and rebuilds the person
    /// it was added to, so their eras learn from it.
    /// </summary>
    private async Task ConfirmAlongsideAsync(int faceId, int personId)
    {
        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = faceId,
            PersonId = personId,
            Source = AssignmentSource.Confirmed,
            DecidedUtc = s_start,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await NewHandler().RebuildErasAsync(personId);
    }

    /// <summary>
    /// Confirms one row as somebody without carrying it to the photograph's other
    /// copies, and returns who that is.
    /// </summary>
    private async Task<int> ConfirmOnlyAsync(int faceId, string name)
    {
        int personId = await _repository.EnsurePersonAsync(name);
        await _repository.AssignAsync(
            personId, [new ScoredFace(faceId)], AssignmentSource.Confirmed);

        return personId;
    }

    /// <summary>How many confirmed faces one person's eras were built from.</summary>
    private async Task<int> SamplesOf(int personId) =>
        await _db.Set<PersonEra>()
            .Where(era => era.PersonId == personId)
            .SumAsync(era => era.SampleCount);

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

    /// <summary>A second context on the same index, as another screen's write would use.</summary>
    private GalleryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
            .Options);

    /// <summary>
    /// The real repository, with a round's write held until the test lets it go.
    /// </summary>
    /// <remarks>
    /// Holds the round between the read it decided from and the write, which is
    /// exactly where an answer given while it runs lands.
    /// </remarks>
    private sealed class HeldAtTheWrite(IPeopleRepository inner) : IPeopleRepository
    {
        private readonly TaskCompletionSource _reached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Reached => _reached.Task;

        public void Release() => _released.TrySetResult();

        public async Task ReplaceProposalsAsync(
            IReadOnlyDictionary<int, IReadOnlyList<ScoredFace>> proposals,
            CancellationToken cancellationToken = default)
        {
            _reached.TrySetResult();
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            await inner.ReplaceProposalsAsync(proposals, cancellationToken);
        }

        public Task<int> EnsurePersonAsync(
            string displayName, CancellationToken cancellationToken = default) =>
            inner.EnsurePersonAsync(displayName, cancellationToken);

        public Task RenamePersonAsync(
            int personId, string displayName, CancellationToken cancellationToken = default) =>
            inner.RenamePersonAsync(personId, displayName, cancellationToken);

        public Task SetBirthYearAsync(
            int personId, int? birthYear, CancellationToken cancellationToken = default) =>
            inner.SetBirthYearAsync(personId, birthYear, cancellationToken);

        public Task AssignAsync(
            int personId,
            IReadOnlyList<ScoredFace> faces,
            AssignmentSource source,
            CancellationToken cancellationToken = default) =>
            inner.AssignAsync(personId, faces, source, cancellationToken);

        public Task UnassignAsync(
            IReadOnlyList<int> faceIds, CancellationToken cancellationToken = default) =>
            inner.UnassignAsync(faceIds, cancellationToken);

        public Task SetIgnoredAsync(
            IReadOnlyList<int> faceIds, bool ignored, CancellationToken cancellationToken = default) =>
            inner.SetIgnoredAsync(faceIds, ignored, cancellationToken);

        public Task ReplaceErasAsync(
            int personId, IReadOnlyList<PersonEra> eras, CancellationToken cancellationToken = default) =>
            inner.ReplaceErasAsync(personId, eras, cancellationToken);

        public Task RemovePersonAsync(int personId, CancellationToken cancellationToken = default) =>
            inner.RemovePersonAsync(personId, cancellationToken);
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
