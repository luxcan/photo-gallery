using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Sharing;

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
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Other = new(200, 60, 50, 50);

    /// <summary>When somebody cropped a photograph on the share, in these tests.</summary>
    private static readonly DateTime Cropped = new(2026, 3, 9, 18, 30, 0, DateTimeKind.Utc);

    /// <summary>How long the cropped file is, which is never what it was.</summary>
    private const long CroppedLength = 777_000;

    /// <summary>The picture both machines have of a photograph, in the pure tests.</summary>
    private const string Picture = "aa11.jpg";

    private static readonly MachineIdentity Hers = new(Guid.NewGuid(), "Mum's laptop", "1.0.0", 1);

    private static readonly MachineIdentity Grans = new(Guid.NewGuid(), "Gran's laptop", "1.0.0", 1);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task FacesFoundOnOneMachineArriveOnAnotherThatHasThePhotograph()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

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
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");

        await Mum.Pooling.GiveAsync();
        await Dad.Pooling.TakeAsync();

        Assert.NotNull(Dad.Db.Assets.AsNoTracking().Single().FacesDetectedUtc);
    }

    [Fact]
    public async Task AFaceIsCreatedRatherThanFilledIn()
    {
        // The one place the pool's own rule does not transfer. A machine that
        // has never run detection has no face rows for a vector to attach to, so
        // refusing to make them would leave it running the full pass anyway and
        // the transfer would have been decoration.
        Asset hers = Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Mum.Face(hers, Head);
        Mum.Face(hers, Other);

        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        Assert.Empty(Dad.Db.Faces);

        await Mum.Pooling.GiveAsync();
        await Dad.Pooling.TakeAsync();

        Assert.Equal(2, Dad.Db.Faces.Count());
    }

    [Fact]
    public async Task FacesForAPhotographThisMachineHasNotIndexedAreNotTaken()
    {
        Mum.Face(Mum.Photo(@"2026 Phone Dump\b.jpg"), Head);

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Empty(Dad.Db.Faces);
        Assert.Empty(Dad.Db.Assets);
    }

    [Fact]
    public async Task AFaceThisMachineHasAlreadyFoundIsNotAddedTwice()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Dad.Face(Dad.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Single(Dad.Db.Faces);
    }

    /// <summary>
    /// The same face, drawn a pixel apart by two machines, is one face.
    /// </summary>
    /// <remarks>
    /// The detector runs on the graphics card now, and floating point there moves
    /// a box by a pixel from one machine to the next. Judged by the exact box,
    /// every face both machines found arrives as a second face on the same nose -
    /// nothing throws, because nothing in the index says a photograph cannot hold
    /// two, and the duplicate is silent.
    /// </remarks>
    [Fact]
    public async Task AFaceAnotherMachineDrewAPixelAwayIsNotAddedTwice()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Dad.Face(Dad.Prepared(@"2019\a.jpg", "aa11.jpg"), Head with { X = Head.X + 1 });

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Single(Dad.Db.Faces);
    }

    [Fact]
    public void LandingTakesNoFaceAPixelAwayFromOneFoundHere()
    {
        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(
            [Found(Hers, Pictures.Face(@"2019\a.jpg", x: 10))],
            Showing(Pictures.Holding(Pictures.Face(@"2019\a.jpg", x: 11))),
            [Drew(Hers, Picture, Pictures.Photo(@"2019\a.jpg"))]);

        Assert.Empty(landing);
    }

    /// <summary>
    /// A face lands only on the picture it was drawn on.
    /// </summary>
    /// <remarks>
    /// A rendition is named after the bytes it was made from, so the same name
    /// on both machines is the same picture - and a different one is another
    /// version of the file, where every box would be in the wrong place.
    /// </remarks>
    [Fact]
    public void LandingTakesAFaceOnlyOntoThePictureItWasDrawnOn()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        FaceSet hers = Found(Hers, Pictures.Face(@"2019\a.jpg"));
        PreparedSet drew = Drew(Hers, Picture, photo);

        Assert.Single(VectorAcceptance.Landing(
            [hers], Showing(Pictures.Indexed(photo)), [drew]));

        Assert.Empty(VectorAcceptance.Landing(
            [hers], Showing(Pictures.Indexed(photo), "bb22.jpg"), [drew]));
    }

    [Fact]
    public void LandingTakesNoFaceWrittenBeforeTheFileItWouldLandOn()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        PreparedSet drew = Drew(Hers, Picture, photo);

        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(
            [Found(Hers, Pictures.Face(@"2019\a.jpg"))],
            Showing(Pictures.Indexed(photo)),
            [drew with { Facts = [drew.Facts[0] with { ModifiedUtc = Monday.AddHours(1) }] }]);

        Assert.Empty(landing);
    }

    [Fact]
    public void LandingTakesNoFaceForAPhotographWithNoPictureHere()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(
            [Found(Hers, Pictures.Face(@"2019\a.jpg"))],
            Pictures.Indexed(photo),
            [Drew(Hers, Picture, photo)]);

        Assert.Empty(landing);
    }

    /// <summary>
    /// Faces from a machine whose facts are not here wait until they are.
    /// </summary>
    /// <remarks>
    /// Usually a facts file caught being written. Without it nothing says which
    /// picture the faces were drawn on, and one more scan costs nothing.
    /// </remarks>
    [Fact]
    public void LandingTakesNoFaceFromAMachineWhoseFactsAreNotHere()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(
            [Found(Hers, Pictures.Face(@"2019\a.jpg"))],
            Showing(Pictures.Indexed(photo)),
            [Drew(Grans, Picture, photo)]);

        Assert.Empty(landing);
    }

    [Fact]
    public void LandingTakesNoFaceOntoAPhotographWhoseFacesHereAreOutOfDate()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(
            [Found(Hers, Pictures.Face(@"2019\a.jpg", x: 10))],
            Showing(Pictures.Holding(Pictures.Face(@"2019\a.jpg", x: 300))) with
            {
                Outdated = new HashSet<AssetKey> { photo },
            },
            [Drew(Hers, Picture, photo)]);

        Assert.Empty(landing);
    }

    /// <summary>
    /// Two other machines' copies of one face land once between them.
    /// </summary>
    /// <remarks>
    /// The first one to land is a face here by the time the second is weighed,
    /// so it is judged against the same rule as one this machine found itself.
    /// </remarks>
    [Fact]
    public void TwoMachinesCopiesOfOneFaceLandOnce()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(
            [
                Found(Hers, Pictures.Face(@"2019\a.jpg", x: 10)),
                Found(Grans, Pictures.Face(@"2019\a.jpg", x: 11)),
            ],
            Showing(Pictures.Indexed(photo)),
            [Drew(Hers, Picture, photo), Drew(Grans, Picture, photo)]);

        Assert.Single(landing);
    }

    /// <summary>
    /// The line is where the answer landing draws it, to the boundary.
    /// </summary>
    /// <remarks>
    /// A face skipped here as already found is one a waiting name has to be able
    /// to land on, and the answers find their face with
    /// <see cref="FaceMatching.Find"/>, which wants more than half. Skip at
    /// exactly half and the face is never added while the name meant for it is
    /// refused for ever.
    /// </remarks>
    [Fact]
    public void ABoxOverlappingAFaceHereByExactlyHalfIsAnotherFace()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");
        var whole = new FaceBounds(0, 0, 40, 40);
        var half = new FaceBounds(0, 0, 40, 20);

        Assert.Equal(0.5d, FaceMatching.Overlap(whole, half));

        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(
            [Found(Hers, new FaceKey(photo, half))],
            Showing(Pictures.Holding(new FaceKey(photo, whole))),
            [Drew(Hers, Picture, photo)]);

        Assert.Single(landing);
    }

    /// <summary>
    /// A turned photograph's faces are not given.
    /// </summary>
    /// <remarks>
    /// Its boxes are in the turned frame and a face carries no rotation, so the
    /// machine taking them would read them as boxes on the upright picture - and
    /// once it merged the turn itself, it would turn them a second time.
    /// </remarks>
    [Fact]
    public async Task TheFacesOfATurnedPhotographAreNotGiven()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg", rotation: 90), Head);

        await Mum.Pooling.GiveAsync();

        FaceSet hers = Assert.Single(await Dad.Pool.FetchFacesAsync());
        Assert.Empty(hers.Faces);
    }

    /// <summary>
    /// Nor are another machine's faces taken onto a photograph turned here.
    /// </summary>
    /// <remarks>
    /// The other half, and not covered by the first: the machine giving them may
    /// never have turned the photograph, or not heard of the turn yet, so its
    /// boxes are upright ones - and every box on this photograph here is in the
    /// turned frame.
    /// </remarks>
    [Fact]
    public async Task FacesAreNotTakenOntoAPhotographTurnedHere()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg", rotation: 90);

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Empty(Dad.Db.Faces);
    }

    [Fact]
    public void LandingTakesNoFaceOntoAPhotographTurnedHere()
    {
        AssetKey photo = Pictures.Photo(@"2019\a.jpg");

        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(
            [Found(Hers, Pictures.Face(@"2019\a.jpg"))],
            Showing(Pictures.Indexed(photo)) with { Turned = new HashSet<AssetKey> { photo } },
            [Drew(Hers, Picture, photo)]);

        Assert.Empty(landing);
    }

    /// <summary>
    /// A name waiting for its face lands in the run that brings the face.
    /// </summary>
    /// <remarks>
    /// Dad already has the picture, so nothing is fetched - only the face
    /// arrives. The answers that waited on it were swept only when a picture
    /// came, so they went on waiting for a whole scan more, on a face that was
    /// sitting right there.
    /// </remarks>
    [Fact]
    public async Task ANameWaitingForItsFaceLandsInTheRunThatBringsTheFace()
    {
        Face hers = Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Mum.Answer(hers, Mum.Person("Ann"), AssignmentSource.Confirmed, Monday);
        await Mum.Sharing.HandleAsync();

        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        await Dad.Sharing.HandleAsync();
        Assert.NotEmpty(Dad.Db.HeldDecisions);

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(1, taken.Faces);
        Assert.Equal(0, taken.Fetched);

        FaceAssignment landed = Dad.Db.FaceAssignments.AsNoTracking().Single();
        Assert.Equal(AssignmentSource.Confirmed, landed.Source);
        Assert.Equal(Dad.Db.Faces.AsNoTracking().Single().Id, landed.FaceId);
        Assert.Equal(
            "Ann",
            Dad.Db.People.AsNoTracking().Single(person => person.Id == landed.PersonId).DisplayName);
        Assert.Empty(Dad.Db.HeldDecisions);
    }

    /// <summary>
    /// A photograph whose own faces are waiting to be found again takes none
    /// from another computer, and its face pass replaces the old ones.
    /// </summary>
    /// <remarks>
    /// Somebody crops a photograph on the share. The crawl sees the file has
    /// changed and clears the marker that says its faces were looked for, but
    /// leaves the faces themselves: they are the face pass's to replace, later in
    /// the same scan. Taking happens in between. Weighed against those old boxes,
    /// the face the other computer found on the new picture looked new and was
    /// added - and adding it stamped the marker, so the face pass never looked,
    /// and the old face stayed for good in the wrong place on the new picture,
    /// still named, beside the new one.
    /// </remarks>
    [Fact]
    public async Task AFaceIsNotTakenOntoAPhotographWhoseOwnFacesAreAboutToBeFoundAgain()
    {
        Asset his = Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        Face before = Dad.Face(his, Head);
        Dad.Answer(before, Dad.Person("Ann"), AssignmentSource.Confirmed, Monday);

        // Mum scanned the cropped file first: a new picture, and her face pass
        // found the face somewhere else on it.
        Asset hers = Mum.Prepared(@"2019\a.jpg", "bb22.jpg");
        CroppedOn(Mum, hers);
        Mum.Face(hers, Other);
        await Mum.Pooling.GiveAsync();

        await RecrawledAsync(Dad, his);
        PoolResult taken = await Dad.Pooling.TakeAsync();

        // The new picture is taken; the face drawn on it waits for his own pass.
        Assert.Equal(1, taken.Filled);
        Assert.Equal(0, taken.Faces);
        Assert.Null(Dad.Db.Assets.AsNoTracking().Single().FacesDetectedUtc);

        await FindFacesAsync(Dad, Other with { X = Other.X + 1 });

        Face only = Dad.Db.Faces.AsNoTracking().Single();
        Assert.Equal(Other with { X = Other.X + 1 }, only.Bounds);
    }

    /// <summary>
    /// A face is not taken for a photograph whose picture has not arrived here,
    /// and the name waiting for it goes on waiting.
    /// </summary>
    /// <remarks>
    /// The same scan makes the picture itself a phase later, and its face pass
    /// then replaces every face on the photograph with the ones it finds - so a
    /// face taken now is always thrown away. Before it was, the sweep had landed
    /// the name waiting for it and let the waiting answer go, and the name was
    /// lost until somebody next pressed Sync now.
    /// </remarks>
    [Fact]
    public async Task AFaceIsNotTakenForAPhotographWhosePictureHasNotArrived()
    {
        Face hers = Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Mum.Answer(hers, Mum.Person("Ann"), AssignmentSource.Confirmed, Monday);
        await Mum.Sharing.HandleAsync();

        Asset his = Dad.Photo(@"2019\a.jpg");
        his.Status = AssetStatus.Pending;
        Dad.Db.SaveChanges();
        Dad.Db.ChangeTracker.Clear();

        await Dad.Sharing.HandleAsync();
        Assert.NotEmpty(Dad.Db.HeldDecisions);

        // Her copy of the picture never reached the folder: a copy that failed,
        // or another computer writing the same name at the same moment.
        await Mum.Pooling.GiveAsync();
        foreach (string copy in Directory.EnumerateFiles(
                     Path.Combine(_house.SharedFolder, SharedFolderPool.ThumbsFolder),
                     "aa11*",
                     SearchOption.AllDirectories))
        {
            File.Delete(copy);
        }

        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, taken.Fetched);
        Assert.Equal(0, taken.Faces);
        Assert.Empty(Dad.Db.Faces);
        Assert.NotEmpty(Dad.Db.HeldDecisions);
    }

    /// <summary>
    /// A face is not taken on the word of a fact that says its own picture could
    /// not be read.
    /// </summary>
    /// <remarks>
    /// The facts say which picture each face was drawn on, and a fact is about a
    /// picture only when the computer that wrote it has that picture ready. This
    /// build gives no fact for a row that could not be read, so the faces on such
    /// a row land nowhere. The build before gave one, with the old copy's name
    /// still on it, and the faces beside it are read past in just the same way;
    /// the face pass here looks for them itself.
    /// </remarks>
    [Fact]
    public async Task AFaceIsNotTakenOnTheWordOfAFactThatSaysItsPictureCouldNotBeRead()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        await Mum.Pooling.GiveAsync();

        // The same fact, as the build before wrote it once her copy had gone and
        // a scan with the originals out of reach could not make it again.
        PreparedFact ready = Assert.Single(Assert.Single(await Dad.Pool.FetchAsync()).Facts);
        await Mum.Pool.PublishAsync(new PreparedSet(
            await PublishDecisionsHandler.ThisMachineAsync(Mum.Index, CancellationToken.None),
            DateTime.UtcNow,
            [ready with { Status = AssetStatus.Failed }],
            new Dictionary<string, string>()));

        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");
        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Empty(Dad.Db.Faces);
    }

    /// <summary>
    /// Faces drawn on the photograph as it was are not taken onto it as it is.
    /// </summary>
    /// <remarks>
    /// A face travels with no length and no modified time, only the path - so
    /// Mum's faces, found before somebody cropped the file, said nothing to stop
    /// them landing on Dad's copy of the cropped one, where every box is in the
    /// wrong place. What does say which picture they were drawn on is the facts
    /// she left beside them.
    /// </remarks>
    [Fact]
    public async Task FacesDrawnOnAnEarlierVersionOfThePhotographAreNotTaken()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        await Mum.Pooling.GiveAsync();

        // Since then somebody cropped it, and Dad has scanned the new file: his
        // own picture, looked at, and no face where Mum's box is.
        Asset his = Dad.Prepared(@"2019\a.jpg", "bb22.jpg");
        CroppedOn(Dad, his);
        Dad.Face(his, Other);

        PoolResult taken = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Equal(Other, Dad.Db.Faces.AsNoTracking().Single().Bounds);
    }

    /// <summary>
    /// Faces left before a photograph changed are not taken onto its new
    /// picture, even when the facts beside them already name it.
    /// </summary>
    /// <remarks>
    /// A machine's facts are written on every scan and its faces only when they
    /// change - and not at all while another computer is on the build before. So
    /// the faces in the folder can be older than the facts beside them: Mum's,
    /// left before the photograph was cropped, beside facts that already name
    /// her picture of the cropped file. Gran has that same picture, and the old
    /// box would have landed on it in the wrong place.
    /// </remarks>
    [Fact]
    public async Task FacesLeftBeforeAPhotographChangedAreNotTakenOntoItsNewPicture()
    {
        Asset hers = Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Mum.Face(hers, Head);
        await Mum.Pooling.GiveAsync();

        // Dad syncs from the build before, so her faces are held back from now on.
        await Dad.Exchange.PublishAsync(DecisionSet.Empty(
            new MachineIdentity(Dad.MachineId, Dad.Name, "1.0.0", 1), Monday));

        // Somebody crops the photograph after that, and her next scan makes the
        // new picture and finds the face again somewhere else on it.
        DateTime cropped = DateTime.UtcNow.AddHours(1);
        Mum.Db.Assets
            .Where(asset => asset.Id == hers.Id)
            .ExecuteUpdate(setters => setters
                .SetProperty(asset => asset.Length, CroppedLength)
                .SetProperty(asset => asset.ModifiedUtc, cropped)
                .SetProperty(asset => asset.ThumbnailName, "bb22.jpg"));
        Mum.WritePicture("bb22.jpg");
        Mum.Db.Faces.Where(face => face.AssetId == hers.Id).ExecuteDelete();
        Mum.Face(hers, Other);

        PoolResult held = await Mum.Pooling.GiveAsync();
        Assert.False(held.FacesLeft);

        Library gran = _house.Add("Gran's laptop");
        Asset grans = gran.Prepared(@"2019\a.jpg", "bb22.jpg");
        gran.Db.Assets
            .Where(asset => asset.Id == grans.Id)
            .ExecuteUpdate(setters => setters
                .SetProperty(asset => asset.Length, CroppedLength)
                .SetProperty(asset => asset.ModifiedUtc, cropped));

        PoolResult taken = await gran.Pooling.TakeAsync();

        Assert.Equal(0, taken.Faces);
        Assert.Empty(gran.Db.Faces);
    }

    /// <summary>
    /// Faces waiting to be found again are not given.
    /// </summary>
    /// <remarks>
    /// They describe a picture that has gone. Her crawl saw the file change and
    /// her generating phase has made the new picture, but her face pass has not
    /// replaced them yet - stopped, or no face models on this laptop - and the
    /// facts she gives already name the new picture, so another computer would
    /// take the old boxes as boxes on it.
    /// </remarks>
    [Fact]
    public async Task FacesWaitingToBeFoundAgainAreNotGiven()
    {
        Asset hers = Mum.Prepared(@"2019\a.jpg", "aa11.jpg");
        Mum.Face(hers, Head);

        await RecrawledAsync(Mum, hers);
        Mum.Db.Assets
            .Where(asset => asset.Id == hers.Id)
            .ExecuteUpdate(setters => setters
                .SetProperty(asset => asset.ThumbnailName, "bb22.jpg")
                .SetProperty(asset => asset.Status, AssetStatus.Ready));
        Mum.WritePicture("bb22.jpg");

        await Mum.Pooling.GiveAsync();

        Assert.Empty(Assert.Single(await Dad.Pool.FetchFacesAsync()).Faces);
    }

    /// <summary>
    /// Faces are not given while another computer would land them twice - and
    /// the pictures are given anyway.
    /// </summary>
    /// <remarks>
    /// Every copy of the app says 1.0.0, so nothing can tell a laptop it is on
    /// the build before faces were landed by overlap - and that build takes a
    /// face a pixel off one of its own as a second face on the same nose. What
    /// it does say, by saying nothing, is in the answers it last left in the
    /// folder.
    /// </remarks>
    [Fact]
    public async Task FacesWaitWhileAnotherComputerWouldLandThemTwice()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);

        // Dad last synced from the build before: his answers say nothing about
        // how he lands faces.
        await Dad.Exchange.PublishAsync(DecisionSet.Empty(
            new MachineIdentity(Dad.MachineId, Dad.Name, "1.0.0", 1), Monday));

        PoolResult given = await Mum.Pooling.GiveAsync();

        Assert.False(File.Exists(VectorsOf(Mum)), "faces were left for a build that doubles them");
        Assert.Contains("aa11.jpg", await Dad.Pool.NamesAsync());
        Assert.Contains("Dad's laptop", given.Summary, StringComparison.Ordinal);

        // He syncs from this build, and the faces follow on the next run.
        await Dad.Publishing.HandleAsync();
        await Mum.Pooling.GiveAsync();

        Assert.True(File.Exists(VectorsOf(Mum)), "faces were still held back");
    }

    /// <summary>
    /// Answers that cannot be read this time cannot say how their computer lands
    /// faces, so the faces wait for them too.
    /// </summary>
    /// <remarks>
    /// Usually a file caught being written, which comes good on the next scan.
    /// Holding the faces back that long costs nothing; the duplicates the guess
    /// could cost cannot be taken back.
    /// </remarks>
    [Fact]
    public async Task FacesWaitForAnswersThatCannotBeRead()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);

        string answers = Path.Combine(_house.SharedFolder, SharedFolderExchange.AnswersFolder);
        Directory.CreateDirectory(answers);
        await File.WriteAllTextAsync(
            Path.Combine(answers, $"{Dad.MachineId:D}{DecisionSetFile.Extension}"), "half written");

        PoolResult given = await Mum.Pooling.GiveAsync();

        Assert.False(File.Exists(VectorsOf(Mum)), "faces were left beside answers nobody could read");
        Assert.Contains("could not be read", given.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// The faces are written again only when they have changed.
    /// </summary>
    /// <remarks>
    /// Every scan gives now, the file is tens of megabytes on a real library, and
    /// most scans find no new face. Writing it each time would be all of that
    /// across the network to say nothing new.
    /// </remarks>
    [Fact]
    public async Task TheFacesAreNotWrittenAgainUntilTheyChange()
    {
        Mum.Face(Mum.Photo(@"2019\a.jpg"), Head);
        DateTime untouched = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        PoolResult first = await Mum.Pooling.GiveAsync();
        File.SetLastWriteTimeUtc(VectorsOf(Mum), untouched);

        PoolResult again = await Mum.Pooling.GiveAsync();

        Assert.True(first.FacesLeft);
        Assert.False(again.FacesLeft);
        Assert.Equal(untouched, File.GetLastWriteTimeUtc(VectorsOf(Mum)));

        // A face found since, and the file is written again.
        Mum.Face(Mum.Photo(@"2019\b.jpg"), Head);
        PoolResult changed = await Mum.Pooling.GiveAsync();

        Assert.True(changed.FacesLeft);
        Assert.NotEqual(untouched, File.GetLastWriteTimeUtc(VectorsOf(Mum)));
        Assert.Equal(2, Assert.Single(await Dad.Pool.FetchFacesAsync()).Faces.Count);
    }

    /// <summary>
    /// A faces file somebody took out of the folder is put back, whatever this
    /// library remembers writing.
    /// </summary>
    [Fact]
    public async Task AFacesFileThatHasGoneIsWrittenAgain()
    {
        Mum.Face(Mum.Photo(@"2019\a.jpg"), Head);

        await Mum.Pooling.GiveAsync();
        File.Delete(VectorsOf(Mum));

        PoolResult again = await Mum.Pooling.GiveAsync();

        Assert.True(again.FacesLeft);
        Assert.True(File.Exists(VectorsOf(Mum)));
    }

    [Fact]
    public async Task RunningItTwiceTakesNoFacesTheSecondTime()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");

        await Mum.Pooling.GiveAsync();
        await Dad.Pooling.TakeAsync();

        PoolResult again = await Dad.Pooling.TakeAsync();

        Assert.Equal(0, again.Faces);
        Assert.Single(Dad.Db.Faces);
    }

    [Fact]
    public async Task VectorsFromAMachineRunningADifferentModelAreRefusedByName()
    {
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");

        Dad.Models.Running(ModelId.FaceRecognition, "recognise-v2");

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

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

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

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
        Mum.Face(Mum.Prepared(@"2019\a.jpg", "aa11.jpg"), Head);
        Dad.Prepared(@"2019\a.jpg", "aa11.jpg");

        Dad.Models.Without(ModelId.FaceDetection).Without(ModelId.FaceRecognition);

        await Mum.Pooling.GiveAsync();
        PoolResult taken = await Dad.Pooling.TakeAsync();

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

    /// <summary>Where a library's faces sit in the folder between them.</summary>
    private string VectorsOf(Library library) =>
        Path.Combine(
            _house.SharedFolder,
            SharedFolderPool.VectorsFolder,
            $"{library.MachineId:D}{SharedFolderPool.VectorsExtension}");

    /// <summary>One machine's vectors, holding these faces and nothing else.</summary>
    private static FaceSet Found(MachineIdentity machine, params FaceKey[] faces) =>
        new(
            machine,
            Monday,
            new Dictionary<string, string>(),
            [.. faces.Select(face => new SharedFace(face, 0.99f, TestEmbeddings.At(0)))]);

    /// <summary>
    /// The facts a machine left beside its faces: the picture it had of each of
    /// these photographs.
    /// </summary>
    private static PreparedSet Drew(MachineIdentity machine, string picture, params AssetKey[] photos) =>
        new(
            machine,
            Monday,
            [
                .. photos.Select(photo => new PreparedFact(
                    photo, 1024, Monday, null, picture, 1000, 800,
                    null, null, null, null, null, AssetStatus.Ready, [])),
            ],
            new Dictionary<string, string>());

    /// <summary>The same library, with this picture of every photograph on its disk.</summary>
    private static LibraryContents Showing(LibraryContents here, string picture = Picture) =>
        here with { Renditions = here.Photographs.ToDictionary(photo => photo, _ => picture) };

    /// <summary>
    /// A library that has already scanned the cropped file, so its row says so.
    /// </summary>
    private static void CroppedOn(Library library, Asset row) =>
        library.Db.Assets
            .Where(asset => asset.Id == row.Id)
            .ExecuteUpdate(setters => setters
                .SetProperty(asset => asset.Length, CroppedLength)
                .SetProperty(asset => asset.ModifiedUtc, Cropped));

    /// <summary>
    /// What a crawl does to a row whose file it finds cropped, through the code
    /// that does it.
    /// </summary>
    private static async Task RecrawledAsync(Library library, Asset row)
    {
        await new SqliteAssetRepository(library.Db).UpdateRangeAsync(
        [
            new Asset
            {
                Id = row.Id,
                PhotoSourceId = row.PhotoSourceId,
                RelativePath = row.RelativePath,
                Length = CroppedLength,
                ModifiedUtc = Cropped,
                CreatedUtc = row.CreatedUtc,
                Kind = AssetKind.Photo,
                Status = AssetStatus.Pending,
            },
        ]);

        library.Db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Runs a library's own face pass, with a detector that finds one face on
    /// every picture it reads.
    /// </summary>
    private static async Task FindFacesAsync(Library library, FaceBounds found)
    {
        var pass = new DetectFacesHandler(
            new SqliteGalleryReader(library.Db),
            library.Thumbnails,
            new FindsOneFace(found),
            new SqliteFaceRepository(library.Db),
            library.Models);

        await pass.HandleAsync();
        library.Db.ChangeTracker.Clear();
    }

    public void Dispose() => _house.Dispose();

    /// <summary>A detector that finds the same one face on every picture.</summary>
    private sealed class FindsOneFace : IFaceScanner
    {
        private readonly FaceBounds _found;

        public FindsOneFace(FaceBounds found) => _found = found;

        public Task<IReadOnlyList<ScannedFace>?> ScanAsync(
            string previewPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScannedFace>?>(
                [new ScannedFace(_found, 0.99f, TestEmbeddings.At(0))]);
    }
}
