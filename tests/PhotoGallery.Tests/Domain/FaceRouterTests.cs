using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Tests.Domain;

/// <summary>
/// Who an unnamed face is offered to. Embeddings sit on a circle, so every
/// expectation here is the cosine of an angle rather than a judgement.
/// </summary>
public sealed class FaceRouterTests
{
    private static readonly DateTime s_when = new(2018, 5, 4, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Route_GivesTheFaceToWhoeverItLooksMostLike()
    {
        // 45 degrees is 0.73 against Anna and 0.99 against Ben. Both clear the
        // threshold; only one of them is right.
        Person anna = PersonAt(1, 0);
        Person ben = PersonAt(2, 52);

        RoutedFace? routed = FaceRouter.Route(s_when, TestEmbeddings.At(45), [anna, ben]);

        Assert.Equal(2, routed?.PersonId);
    }

    [Fact]
    public void Route_DoesNotDependOnTheOrderThePeopleAreGivenIn()
    {
        // The defect this type replaced: a per-person threshold test let the
        // first person read take the face, so the answer changed with the
        // alphabet.
        Person anna = PersonAt(1, 0);
        Person ben = PersonAt(2, 52);

        Assert.Equal(
            FaceRouter.Route(s_when, TestEmbeddings.At(45), [anna, ben])?.PersonId,
            FaceRouter.Route(s_when, TestEmbeddings.At(45), [ben, anna])?.PersonId);
    }

    [Fact]
    public void Route_AsksNobodyWhenTheBestTwoAreTooCloseTogether()
    {
        // Halfway between two people. Both score 0.91, which is not a weak match
        // for either - it is a question that cannot be answered, and the app
        // says so by staying quiet.
        Person anna = PersonAt(1, 0);
        Person ben = PersonAt(2, 50);

        Assert.Null(FaceRouter.Route(s_when, TestEmbeddings.At(25), [anna, ben]));
    }

    [Fact]
    public void Route_StillAnswersWhenThereIsOnlyOneCandidate()
    {
        // Nobody to be confused with means the margin has nothing to say. A
        // library with one named person must still get proposals.
        Person anna = PersonAt(1, 0);

        Assert.Equal(1, FaceRouter.Route(s_when, TestEmbeddings.At(30), [anna])?.PersonId);
    }

    [Fact]
    public void Route_OffersNobodyWhenTheClosestPersonIsStillNotClose()
    {
        // 80 degrees is 0.17 - the range where two unrelated people sit.
        Person anna = PersonAt(1, 0);

        Assert.Null(FaceRouter.Route(s_when, TestEmbeddings.At(80), [anna]));
    }

    [Fact]
    public void Route_DropsARefusedPersonAndLetsTheFaceFallToTheNext()
    {
        // "Not Anna" is not "nobody". Taking her out of the running is what lets
        // the runner-up be offered the face instead of it going quiet.
        Person anna = PersonAt(1, 0);
        Person ben = PersonAt(2, 52);

        RoutedFace? routed = FaceRouter.Route(
            s_when, TestEmbeddings.At(5), [anna, ben], personId => personId != 1);

        Assert.Equal(2, routed?.PersonId);
    }

    [Fact]
    public void Route_ComparesEachPersonAtTheAgeTheyWereWhenThePictureWasTaken()
    {
        // A child who changes has more than one era, and the face is weighed
        // against the one covering the date - not against their whole life
        // averaged into a blur.
        var child = new Person { Id = 1, DisplayName = "Ana Lim" };
        child.Eras.Add(EraOver(new DateTime(2015, 1, 1), new DateTime(2016, 1, 1), 0));
        child.Eras.Add(EraOver(new DateTime(2016, 1, 1), new DateTime(2020, 1, 1), 70));

        Assert.Equal(1, FaceRouter.Route(
            new DateTime(2015, 6, 1), TestEmbeddings.At(5), [child])?.PersonId);

        Assert.Null(FaceRouter.Route(
            new DateTime(2018, 6, 1), TestEmbeddings.At(5), [child]));
    }

    [Fact]
    public void Route_IgnoresSomebodyWithNoErasAtAll()
    {
        // Somebody added by name and never pointed out in a picture. They have
        // nothing to compare against, and must not be a candidate.
        var unknown = new Person { Id = 1, DisplayName = "Nobody yet" };
        Person anna = PersonAt(2, 0);

        Assert.Equal(2, FaceRouter.Route(
            s_when, TestEmbeddings.At(10), [unknown, anna])?.PersonId);
    }

    [Fact]
    public void Route_ReportsHowClearCutTheAnswerWas()
    {
        Person anna = PersonAt(1, 0);
        Person ben = PersonAt(2, 52);

        RoutedFace routed = FaceRouter.Route(s_when, TestEmbeddings.At(45), [anna, ben])!.Value;

        Assert.Equal(Math.Cos(45 * Math.PI / 180), routed.Runner, 3);
        Assert.Equal(routed.Score - routed.Runner, routed.Lead, 5);
    }

    private static Person PersonAt(int id, double degrees)
    {
        var person = new Person { Id = id, DisplayName = $"Person {id}" };
        person.Eras.Add(EraOver(s_when.AddYears(-1), s_when.AddYears(1), degrees));
        return person;
    }

    private static PersonEra EraOver(DateTime from, DateTime to, double degrees) => new()
    {
        FromUtc = from,
        ToUtc = to,
        Centroid = TestEmbeddings.At(degrees),
        SampleCount = 20,
    };
}
