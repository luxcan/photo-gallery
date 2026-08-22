using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Straightening a photograph moves three things together - the cached copies,
/// the boxes drawn on them, and the turn recorded against the row - and any one
/// of them moving alone is a bug the user would see.
/// </summary>
public sealed class TurnPhotoHandlerTests : IDisposable
{
    private const string Shared = "aabbccddeeff00112233445566778899.jpg";

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly FakeTurner _turner = new();
    private readonly FakeOriginals _originals = new();
    private readonly FakeAvailability _sources = new();
    private readonly TurnPhotoHandler _handler;

    public TurnPhotoHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-turn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
            .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = @"C:\pictures" });
        _db.SaveChanges();

        _handler = new TurnPhotoHandler(
            _turner,
            _originals,
            new SqliteAssetRepository(_db),
            new SqliteFaceRepository(_db),
            _sources);
    }

    [Fact]
    public async Task Turn_MovesTheFaceBoxesWithThePicture()
    {
        // The point of the whole design. Re-detecting a straightened picture
        // would replace its faces and take every confirmed name with them.
        int assetId = AddPhoto(@"a\upside-down.jpg");
        AddFace(assetId, new FaceBounds(10, 5, 20, 30));

        Assert.True((await _handler.HandleAsync(Shared, 90)).Turned);

        Face face = await _db.Faces.SingleAsync();
        Assert.Equal(new FaceBounds(600 - 5 - 30, 10, 30, 20), face.Bounds);
    }

    [Fact]
    public async Task Turn_KeepsTheNameOnAFaceItMoved()
    {
        int assetId = AddPhoto(@"a\upside-down.jpg");
        int faceId = AddFace(assetId, new FaceBounds(10, 5, 20, 30));
        int personId = AddPersonNamedOn(faceId);

        await _handler.HandleAsync(Shared, 180);

        FaceAssignment assignment = await _db.FaceAssignments.SingleAsync();
        Assert.Equal(personId, assignment.PersonId);
        Assert.Equal(AssignmentSource.Confirmed, assignment.Source);
    }

    [Fact]
    public async Task Turn_RecordsTheTurnSoALaterPassReappliesIt()
    {
        int assetId = AddPhoto(@"a\upside-down.jpg");

        await _handler.HandleAsync(Shared, 90);

        Assert.Equal(90, (await _db.Assets.SingleAsync(a => a.Id == assetId)).Rotation);
    }

    [Fact]
    public async Task Turn_FourTimesIsNoTurnAtAll()
    {
        // Folded back into a quarter, so the preparation pass never has to work
        // through a full circle to arrive where it started.
        int assetId = AddPhoto(@"a\upside-down.jpg");

        for (int i = 0; i < 4; i++)
        {
            await _handler.HandleAsync(Shared, 90);
        }

        Assert.Equal(0, (await _db.Assets.SingleAsync(a => a.Id == assetId)).Rotation);
    }

    [Fact]
    public async Task Turn_AnticlockwiseIsRecordedAsThreeQuartersClockwise()
    {
        int assetId = AddPhoto(@"a\upside-down.jpg");

        await _handler.HandleAsync(Shared, -90);

        Assert.Equal(270, (await _db.Assets.SingleAsync(a => a.Id == assetId)).Rotation);
    }

    [Fact]
    public async Task Turn_AppliesToEveryCopyThatSharesTheOnePicture()
    {
        // Renditions are named after the picture's content, so duplicates share
        // one file - 245 of them in the measured library. Turning that file turns
        // it for every row, and a row left behind would draw the same picture
        // while disagreeing about which way up it is.
        int first = AddPhoto(@"a\one.jpg");
        int second = AddPhoto(@"b\the-same-again.jpg");
        AddFace(first, new FaceBounds(10, 5, 20, 30));
        AddFace(second, new FaceBounds(10, 5, 20, 30));

        await _handler.HandleAsync(Shared, 180);

        Assert.Equal([180, 180], await _db.Assets.Select(a => a.Rotation).ToListAsync());
        Assert.All(
            await _db.Faces.ToListAsync(),
            face => Assert.Equal(new FaceBounds(800 - 10 - 20, 600 - 5 - 30, 20, 30), face.Bounds));
    }

    [Fact]
    public async Task Turn_ChangesNothingWhenTheCachedPictureCannotBeRead()
    {
        // A rendition that is missing or corrupt. Recording a turn against it
        // would leave the row claiming something the picture does not show.
        int assetId = AddPhoto(@"a\one.jpg");
        AddFace(assetId, new FaceBounds(10, 5, 20, 30));
        _turner.Fails = true;

        Assert.False((await _handler.HandleAsync(Shared, 90)).Turned);

        Assert.Equal(0, (await _db.Assets.SingleAsync()).Rotation);
        Assert.Equal(new FaceBounds(10, 5, 20, 30), (await _db.Faces.SingleAsync()).Bounds);
    }

    [Fact]
    public async Task Turn_OfNothingIsRefusedRatherThanRecorded()
    {
        AddPhoto(@"a\one.jpg");

        Assert.False((await _handler.HandleAsync(Shared, 0)).Turned);
        Assert.False((await _handler.HandleAsync(null, 90)).Turned);
        Assert.False((await _handler.HandleAsync("   ", 90)).Turned);

        Assert.Equal(0, (await _db.Assets.SingleAsync()).Rotation);
    }

    [Fact]
    public async Task Turn_TellsTheFileItselfWhenItCanHoldTheAnswer()
    {
        // The file becomes self-describing, so the app clears its own override:
        // a picture that is upright because its own tag says so must not also be
        // turned again by this app on top of it.
        int assetId = AddPhoto(@"a\upside-down.jpg");
        _originals.Accepts = true;

        TurnedPhoto result = await _handler.HandleAsync(Shared, 90);

        Assert.Equal(1, result.OriginalsTold);
        Assert.Equal(0, result.CachedOnly);
        Assert.Equal(0, (await _db.Assets.SingleAsync(a => a.Id == assetId)).Rotation);
        Assert.Equal([@"C:\pictures\a\upside-down.jpg"], _originals.Asked);
    }

    [Fact]
    public async Task Turn_KeepsItsOwnOverrideWhenTheFileCannotHoldTheAnswer()
    {
        // A JPEG with no orientation tag has no room to add one, so this app
        // remembering the turn is the only thing keeping the picture upright.
        int assetId = AddPhoto(@"a\upside-down.jpg");
        _originals.Accepts = false;

        TurnedPhoto result = await _handler.HandleAsync(Shared, 90);

        Assert.Equal(0, result.OriginalsTold);
        Assert.Equal(1, result.CachedOnly);
        Assert.Equal(90, (await _db.Assets.SingleAsync(a => a.Id == assetId)).Rotation);
    }

    [Fact]
    public async Task Turn_AsksEachDuplicateSeparately()
    {
        // Two rows share one picture but are two files on disk, and one can hold
        // a tag while the other cannot. Deciding once for both would leave a row
        // claiming something untrue about its own file.
        AddPhoto(@"a\one.jpg");
        AddPhoto(@"b\the-same-again.jpg");
        _originals.Accepts = true;

        await _handler.HandleAsync(Shared, 90);

        Assert.Equal(
            [@"C:\pictures\a\one.jpg", @"C:\pictures\b\the-same-again.jpg"],
            _originals.Asked.Order());
    }

    /// <summary>
    /// A share that is away is not the same as a file that cannot hold a tag,
    /// and the app must not report the second when it means the first.
    /// </summary>
    /// <remarks>
    /// Before this check, turning a photograph on an absent share turned the
    /// cached copies happily - they are local - and only the original refused,
    /// which put the row into "this app remembers the turn, the file cannot" and
    /// badged the picture "Turned here only" with a tooltip saying the file
    /// cannot record which way up it goes. The file most likely can; nobody was
    /// able to ask it. The override then persisted, so the picture stayed marked
    /// un-correctable long after the share came back.
    /// </remarks>
    [Fact]
    public async Task Turn_ChangesNothingWhenTheSourceCannotBeReached()
    {
        int assetId = AddPhoto(@"a\upside-down.jpg");
        AddFace(assetId, new FaceBounds(10, 5, 20, 30));
        _sources.Away.Add(@"C:\pictures");

        TurnedPhoto result = await _handler.HandleAsync(Shared, 90);

        Assert.False(result.Turned);
        Assert.Equal([@"C:\pictures"], result.UnreachableSources);

        // Said apart from CachedOnly, which means the file was asked and said no.
        Assert.Equal(0, result.CachedOnly);
        Assert.Equal(0, result.OriginalsTold);

        // The cached copies are local and would have turned quite happily. The
        // check has to come before them, not after.
        Assert.Equal(0, _turner.Turns);
        Assert.Empty(_originals.Asked);

        Assert.Equal(0, (await _db.Assets.SingleAsync()).Rotation);
        Assert.Equal(new FaceBounds(10, 5, 20, 30), (await _db.Faces.SingleAsync()).Bounds);
    }

    [Fact]
    public async Task Turn_RefusesWhenAnyOfTheSharedCopiesIsOutOfReach()
    {
        // Two rows share one rendition across two sources. Turning it while only
        // one can be told would leave the other drawing the same picture and
        // disagreeing about which way up it is.
        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 2, Path = @"D:\more-pictures" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        AddPhoto(@"a\one.jpg");
        AddPhoto(@"b\the-same-again.jpg", photoSourceId: 2);
        _sources.Away.Add(@"D:\more-pictures");

        TurnedPhoto result = await _handler.HandleAsync(Shared, 90);

        Assert.False(result.Turned);
        Assert.Equal([@"D:\more-pictures"], result.UnreachableSources);
        Assert.Equal(0, _turner.Turns);
        Assert.All(await _db.Assets.ToListAsync(), asset => Assert.Equal(0, asset.Rotation));
    }

    private int AddPhoto(string relativePath, int photoSourceId = 1)
    {
        var asset = new Asset
        {
            PhotoSourceId = photoSourceId,
            RelativePath = relativePath,
            Length = 1,
            ModifiedUtc = new DateTime(2018, 5, 4),
            CreatedUtc = new DateTime(2018, 5, 4),
            IndexedUtc = new DateTime(2018, 5, 4),
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = Shared,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return asset.Id;
    }

    private int AddFace(int assetId, FaceBounds bounds)
    {
        var face = new Face
        {
            AssetId = assetId,
            Bounds = bounds,
            DetectScore = 0.9f,
            Embedding = TestEmbeddings.At(0),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return face.Id;
    }

    private int AddPersonNamedOn(int faceId)
    {
        var person = new Person { DisplayName = "Ana Lim" };
        _db.Set<Person>().Add(person);
        _db.SaveChanges();

        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = faceId,
            PersonId = person.Id,
            Source = AssignmentSource.Confirmed,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return person.Id;
    }

    /// <summary>A preview 800 by 600, or one that will not read.</summary>
    private sealed class FakeTurner : IRenditionTurner
    {
        public bool Fails { get; set; }

        public int Turns { get; private set; }

        public TurnedRendition? Turn(string thumbnailName, int degrees)
        {
            Turns++;
            return Fails ? null : new TurnedRendition(800, 600);
        }
    }

    /// <summary>Sources that are up, and sources that are away.</summary>
    private sealed class FakeAvailability : ISourceAvailability
    {
        public HashSet<string> Away { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool CanReach(string sourceRoot) => !Away.Contains(sourceRoot);
    }

    /// <summary>
    /// A file that either can be told which way up it goes, or cannot.
    /// </summary>
    /// <remarks>
    /// Refusing by default, because that is the case the app's own override
    /// exists for and the one a test that forgets to say would otherwise pass
    /// without exercising.
    /// </remarks>
    private sealed class FakeOriginals : IOriginalOrientation
    {
        public bool Accepts { get; set; }

        public List<string> Asked { get; } = [];

        public bool TryTurn(string fullPath, int degrees)
        {
            Asked.Add(fullPath);
            return Accepts;
        }
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
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
