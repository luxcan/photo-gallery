using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// The faces one machine has already found, taken by another - and refused when
/// the models differ.
/// </summary>
/// <remarks>
/// Two hours of detection on this library against seconds of copying, and the
/// whole of it rests on the fingerprint. An embedding is meaningless outside the
/// model that produced it, and a mismatched one does not fail: it answers
/// confidently about the wrong person, which looks exactly like a right answer.
/// </remarks>
public sealed class FaceVectorsTests : IDisposable
{
    private static readonly FaceBounds Head = new(10, 10, 40, 40);
    private static readonly FaceBounds Other = new(200, 60, 50, 50);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task FacesFoundOnOneMachineArriveOnAnotherThatHasThePhotograph()
    {
        Mum.Face(Mum.Photo(@"2019\a.jpg"), Head);
        Dad.Photo(@"2019\a.jpg");

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(1, taken.Faces);

        Face arrived = Dad.Db.Faces.AsNoTracking().Single();
        Assert.Equal(Head, arrived.Bounds);

        // The vector itself, which is the part worth two hours.
        Assert.Equal(
            Mum.Db.Faces.AsNoTracking().Single().Embedding.Values.ToArray(),
            arrived.Embedding.Values.ToArray());
    }

    [Fact]
    public async Task AndThatPhotographIsNotLookedAtForFacesAgain()
    {
        // The stamp is not decoration: the detection pass selects on it, so a
        // row left null would be read and detected again on the next scan -
        // which is the whole two hours coming back, having just been avoided.
        Mum.Face(Mum.Photo(@"2019\a.jpg"), Head);
        Dad.Photo(@"2019\a.jpg");

        await Mum.Pooling.HandleAsync();
        await Dad.Pooling.HandleAsync();

        Assert.NotNull(Dad.Db.Assets.AsNoTracking().Single().FacesDetectedUtc);
    }

    [Fact]
    public async Task AFaceIsCreatedRatherThanFilledIn()
    {
        // The one place the pool's own rule does not transfer. A machine that
        // has never run detection has no face rows for a vector to attach to, so
        // refusing to make them would leave it running the full pass anyway and
        // the transfer would have been decoration.
        Asset hers = Mum.Photo(@"2019\a.jpg");
        Mum.Face(hers, Head);
        Mum.Face(hers, Other);

        Dad.Photo(@"2019\a.jpg");
        Assert.Empty(Dad.Db.Faces);

        await Mum.Pooling.HandleAsync();
        await Dad.Pooling.HandleAsync();

        Assert.Equal(2, Dad.Db.Faces.Count());
    }

    [Fact]
    public async Task FacesForAPhotographThisMachineHasNotIndexedAreNotTaken()
    {
        Mum.Face(Mum.Photo(@"2026 Phone Dump\b.jpg"), Head);

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Empty(Dad.Db.Faces);
        Assert.Empty(Dad.Db.Assets);
    }

    [Fact]
    public async Task AFaceThisMachineHasAlreadyFoundIsNotAddedTwice()
    {
        Mum.Face(Mum.Photo(@"2019\a.jpg"), Head);
        Dad.Face(Dad.Photo(@"2019\a.jpg"), Head);

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Single(Dad.Db.Faces);
    }

    [Fact]
    public async Task RunningItTwiceTakesNoFacesTheSecondTime()
    {
        Mum.Face(Mum.Photo(@"2019\a.jpg"), Head);
        Dad.Photo(@"2019\a.jpg");

        await Mum.Pooling.HandleAsync();
        await Dad.Pooling.HandleAsync();

        PoolResult again = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, again.Faces);
        Assert.Single(Dad.Db.Faces);
    }

    [Fact]
    public async Task VectorsFromAMachineRunningADifferentModelAreRefusedByName()
    {
        Mum.Face(Mum.Photo(@"2019\a.jpg"), Head);
        Dad.Photo(@"2019\a.jpg");

        Dad.Models.Running(ModelId.FaceRecognition, "recognise-v2");

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Empty(Dad.Db.Faces);

        ModelMismatch refused = Assert.Single(taken.Mismatches);
        Assert.Equal("Mum's laptop", refused.Machine.Name);
        Assert.Equal(["FaceRecognition"], refused.Models);

        // Named on screen, because "some vectors were refused" is a message
        // nobody can act on.
        Assert.Contains("Mum's laptop is running a different", taken.Summary);
        Assert.Contains("FaceRecognition", taken.Summary);
    }

    [Fact]
    public async Task AndTheDecisionsAndPicturesAreStillTaken()
    {
        // The refusal is about vectors and nothing else. Neither a decision nor
        // a rendition depends on a model at all.
        Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Mum.Face(Mum.Db.Assets.Single(), Head);
        Dad.Photo(@"2019\a.jpg");

        Dad.Models.Running(ModelId.FaceDetection, "detect-v9");

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Single(taken.Mismatches);

        // The picture and the facts crossed anyway.
        Assert.Equal(1, taken.Filled);
        Assert.True(Dad.Holds("aa11.jpg"));
        Assert.Equal("aa11.jpg", Dad.Db.Assets.AsNoTracking().Single().ThumbnailName);
    }

    [Fact]
    public async Task AMachineThatHasNotInstalledTheModelsStillTakesTheFaces()
    {
        // Not a disagreement: it has no vectors of its own to contradict, and
        // taking somebody else's is the entire point.
        Mum.Face(Mum.Photo(@"2019\a.jpg"), Head);
        Dad.Photo(@"2019\a.jpg");

        Dad.Models.Without(ModelId.FaceDetection).Without(ModelId.FaceRecognition);

        await Mum.Pooling.HandleAsync();
        PoolResult taken = await Dad.Pooling.HandleAsync();

        Assert.Equal(1, taken.Faces);
        Assert.Empty(taken.Mismatches);
    }

    [Fact]
    public void SiftingIsAPureFunctionOfTwoFingerprints()
    {
        var mine = new Dictionary<string, string> { ["FaceRecognition"] = "v1" };

        FaceSet agrees = Set("Mum's laptop", new Dictionary<string, string>
        {
            ["FaceRecognition"] = "v1",
        });

        FaceSet differs = Set("Dad's laptop", new Dictionary<string, string>
        {
            ["FaceRecognition"] = "v2",
        });

        (IReadOnlyList<FaceSet> accepted, IReadOnlyList<ModelMismatch> refused) =
            VectorAcceptance.Sift(mine, [agrees, differs]);

        Assert.Equal("Mum's laptop", Assert.Single(accepted).Machine.Name);
        Assert.Equal("Dad's laptop", Assert.Single(refused).Machine.Name);
    }

    private static FaceSet Set(string name, IReadOnlyDictionary<string, string> models) =>
        new(
            new MachineIdentity(Guid.NewGuid(), name, "1.0.0", 1),
            new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc),
            models,
            []);

    public void Dispose() => _house.Dispose();
}
