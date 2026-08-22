using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Search;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The only thing in this app that destroys something the user cannot get back.
/// What it refuses to do matters more than what it does.
/// </summary>
public sealed class RemovePhotoHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly FileSystemThumbnailStore _thumbnails;
    private readonly FakeOriginal _original = new();
    private readonly FakeAvailability _sources = new();
    private readonly RemovePhotoHandler _handler;

    public RemovePhotoHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-remove-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();
        _thumbnails = new FileSystemThumbnailStore(workingFolder);

        _db = new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
            .Options);
        _db.Database.Migrate();

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = @"C:\pictures" });
        _db.SaveChanges();

        _handler = new RemovePhotoHandler(
            new SqliteAssetRepository(_db), _original, _thumbnails, _sources);
    }

    [Fact]
    public async Task Describe_CountsWhatWouldBeLostSoTheQuestionCanSayIt()
    {
        int assetId = AddPhoto(@"a\one.jpg", "aa11");
        int named = AddFace(assetId);
        AddFace(assetId);
        NameSomebodyOn(named);

        PhotoToRemove? photo = await _handler.DescribeAsync(assetId);

        Assert.NotNull(photo);
        Assert.Equal("one.jpg", photo!.FileName);
        Assert.Equal(@"C:\pictures\a\one.jpg", photo.FullPath);
        Assert.Equal(2, photo.Faces);
        Assert.Equal(1, photo.Names);
    }

    [Fact]
    public async Task Describe_SaysWhetherTheRecycleBinWouldCatchIt()
    {
        // A share has no Recycle Bin, and a dialog implying an undo that does
        // not exist is the worst thing this feature could do.
        int assetId = AddPhoto(@"a\one.jpg", "aa11");

        _original.Recycles = true;
        Assert.True((await _handler.DescribeAsync(assetId))!.Recoverable);

        _original.Recycles = false;
        Assert.False((await _handler.DescribeAsync(assetId))!.Recoverable);
    }

    [Fact]
    public async Task Describe_HasNothingToSayAboutARowThatHasGone() =>
        Assert.Null(await _handler.DescribeAsync(404));

    [Fact]
    public async Task Handle_TakesTheFileTheRowAndEverythingSaidAboutIt()
    {
        int assetId = AddPhoto(@"a\one.jpg", "aa11");
        int faceId = AddFace(assetId);
        NameSomebodyOn(faceId);

        Assert.True(await RemoveAsync(assetId));

        Assert.Equal([@"C:\pictures\a\one.jpg"], _original.Deleted);
        Assert.Equal(0, await _db.Assets.CountAsync());
        Assert.Equal(0, await _db.Faces.CountAsync());
        Assert.Equal(0, await _db.FaceAssignments.CountAsync());

        // The person stays. Deleting one of their photographs is not a request
        // to forget who they are.
        Assert.Equal(1, await _db.Set<Person>().CountAsync());
    }

    [Fact]
    public async Task Handle_KeepsEverythingWhenTheFileWillNotGo()
    {
        // A file open elsewhere, read-only, or on a share that has gone away.
        // Forgetting a photograph still sitting on disk would only mean the next
        // scan re-indexing it, stripped of its names.
        int assetId = AddPhoto(@"a\one.jpg", "aa11");
        AddFace(assetId);
        _original.Refuses = true;

        Assert.False(await RemoveAsync(assetId));

        Assert.Equal(1, await _db.Assets.CountAsync());
        Assert.Equal(1, await _db.Faces.CountAsync());
    }

    [Fact]
    public async Task Handle_LeavesTheCachedPicturesWhileAnotherCopyStillDrawsThem()
    {
        // Renditions are named after the picture's content, so duplicates share
        // one pair of files. Deleting them for this row would leave the other
        // row blank.
        string name = await SaveRenditionAsync("aa11");
        int first = AddPhoto(@"a\one.jpg", name);
        AddPhoto(@"b\the-same-again.jpg", name);

        Assert.True(await RemoveAsync(first));

        Assert.True(_thumbnails.Exists(name), "the surviving copy lost its picture");
        Assert.Equal(1, await _db.Assets.CountAsync());
    }

    [Fact]
    public async Task Handle_TakesTheCachedPicturesWithTheLastCopy()
    {
        string name = await SaveRenditionAsync("bb22");
        int assetId = AddPhoto(@"a\one.jpg", name);

        Assert.True(await RemoveAsync(assetId));

        Assert.False(_thumbnails.Exists(name), "the renditions were left behind");
    }

    [Fact]
    public async Task Handle_ForgetsAPhotographThatHasAlreadyGoneFromDisk()
    {
        // Keeping the row would only offer the user a picture that cannot open.
        int assetId = AddPhoto(@"a\one.jpg", "aa11");
        _original.AlreadyGone = true;

        Assert.True(await RemoveAsync(assetId));
        Assert.Equal(0, await _db.Assets.CountAsync());
    }

    [Fact]
    public async Task Handle_DoesNothingForARowThatHasGone() =>
        Assert.False(await RemoveAsync(404));

    /// <summary>
    /// The photograph is safe on a share that is switched off, and this app must
    /// not read that as permission to forget it.
    /// </summary>
    /// <remarks>
    /// The regression this whole check exists for. <c>File.Exists</c> answers
    /// false for a file that was deleted and for one on a share that is not
    /// there alike - measured against this library's NAS, false in 21 seconds -
    /// so the delete path read "cannot see it" as "already gone", reported
    /// success, and took the row, its faces and every confirmed name with it.
    /// The file was never in danger; only the index was.
    /// </remarks>
    [Fact]
    public async Task Handle_ForgetsNothingWhenTheSourceCannotBeReached()
    {
        int assetId = AddPhoto(@"a\one.jpg", "aa11");
        int faceId = AddFace(assetId);
        NameSomebodyOn(faceId);

        // Exactly what an absent share looks like from here: every question
        // about the file comes back "not there".
        _original.AlreadyGone = true;
        _sources.Away.Add(@"C:\pictures");

        PhotoRemovalResult result = await _handler.HandleAsync([assetId]);

        Assert.Equal(0, result.Deleted);
        Assert.Equal([assetId], result.OutOfReach);
        Assert.Equal([@"C:\pictures"], result.UnreachableSources);

        // Named apart from a refusal, which claims the file is there and would
        // not go - a claim nobody offline is in a position to make.
        Assert.Empty(result.Refused);

        Assert.Equal(1, await _db.Assets.CountAsync());
        Assert.Equal(1, await _db.Faces.CountAsync());
        Assert.Equal(1, await _db.FaceAssignments.CountAsync());
    }

    [Fact]
    public async Task Handle_DoesNotEvenAskTheFileWhenTheSourceIsAway()
    {
        // Not merely "the row survives": nothing is attempted at all, so a share
        // that comes back finds its files exactly as it left them.
        int assetId = AddPhoto(@"a\one.jpg", "aa11");
        _sources.Away.Add(@"C:\pictures");

        await _handler.HandleAsync([assetId]);

        Assert.Empty(_original.Deleted);
    }

    [Fact]
    public async Task Handle_AsksEachSourceOnceHoweverManyPhotographsAreOnIt()
    {
        // Per file this costs a network round trip that takes 21 seconds to fail
        // on an absent share. Four hundred duplicates would be a two-hour freeze
        // before anything was refused.
        int[] photos =
        [
            AddPhoto(@"a\one.jpg", "aa11"),
            AddPhoto(@"a\two.jpg", "bb22"),
            AddPhoto(@"a\three.jpg", "cc33"),
        ];

        await _handler.HandleAsync(photos);

        Assert.Equal([@"C:\pictures"], _sources.Asked);
    }

    [Fact]
    public async Task Handle_StillTakesThePhotographsWhoseSourceIsThere()
    {
        // One share being away is not a reason to abandon the rest of a batch.
        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 2, Path = @"D:\more-pictures" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        int away = AddPhoto(@"a\one.jpg", "aa11");
        int here = AddPhoto(@"b\two.jpg", "bb22", photoSourceId: 2);
        _sources.Away.Add(@"C:\pictures");

        PhotoRemovalResult result = await _handler.HandleAsync([away, here]);

        Assert.Equal(1, result.Deleted);
        Assert.Equal([away], result.OutOfReach);
        Assert.Equal([@"D:\more-pictures\b\two.jpg"], _original.Deleted);
        Assert.Equal(1, await _db.Assets.CountAsync());
    }

    [Fact]
    public async Task UnreachableSources_NamesTheShareBeforeTheQuestionIsPut()
    {
        // So an absent share is met with "nothing was changed" rather than with
        // a confirmation that is granted and then quietly does nothing.
        int assetId = AddPhoto(@"a\one.jpg", "aa11");
        PhotoToRemove photo = (await _handler.DescribeAsync(assetId))!;

        Assert.Empty(_handler.UnreachableSources([photo]));

        _sources.Away.Add(@"C:\pictures");
        Assert.Equal([@"C:\pictures"], _handler.UnreachableSources([photo]));
    }

    [Fact]
    public async Task Handle_NamesEachPhotographBeforeItGoes()
    {
        // The whole point of reporting before rather than after: the shell draws
        // the picture named here, and its rendition has to still be on disk.
        string rendition = await SaveRenditionAsync("cc33");
        int first = AddPhoto(@"a\one.jpg", rendition);
        int second = AddPhoto(@"a\two.jpg", "dd44");

        var reports = new Reports();
        await _handler.HandleAsync([first, second], reports);

        Assert.Equal(["one.jpg", "two.jpg", string.Empty], reports.Seen.Select(p => p.FileName));
        Assert.Equal(rendition, reports.Seen[0].ThumbnailName);
        Assert.Equal([0, 1, 2], reports.Seen.Select(p => p.Done));
        Assert.All(reports.Seen, p => Assert.Equal(2, p.Total));

        // And it finishes full rather than on whichever picture happened to be
        // last, so the bar does not stop short of the end.
        Assert.Equal(1d, reports.Seen[^1].Fraction);
    }

    [Fact]
    public async Task Handle_KeepsGoingPastAFileThatWillNotMove()
    {
        // One locked file must not abandon the rest of a deletion, and it has to
        // be named so the screen that asked can say what is still there.
        int assetId = AddPhoto(@"a\one.jpg", "aa11");
        _original.Refuses = true;

        PhotoRemovalResult result = await _handler.HandleAsync([assetId, 404]);

        Assert.Equal(0, result.Deleted);
        Assert.Equal([assetId, 404], result.Refused);
        Assert.False(result.WasCancelled);
        Assert.Equal(1, await _db.Assets.CountAsync());
    }

    [Fact]
    public async Task Handle_StoppingLeavesWhatItHasNotReachedAlone()
    {
        // Stopped between photographs, never inside one: a row for a file that
        // is no longer there would be resurrected by the next scan, stripped of
        // its names.
        int first = AddPhoto(@"a\one.jpg", "aa11");
        int second = AddPhoto(@"a\two.jpg", "bb22");

        using var stopping = new CancellationTokenSource();
        var pressStop = new Reports(_ => stopping.Cancel());

        PhotoRemovalResult result =
            await _handler.HandleAsync([first, second], pressStop, stopping.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(1, result.Deleted);
        Assert.Empty(result.Refused);

        // The first went whole - file, row and all - and the second was never
        // touched.
        Assert.Equal([@"C:\pictures\a\one.jpg"], _original.Deleted);
        Assert.Equal(1, await _db.Assets.CountAsync());
    }

    /// <summary>
    /// Collects the reports as they are made, on the thread that makes them.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>Progress&lt;T&gt;</c>, which posts to a captured
    /// context - with none in a test that is the thread pool, so its callbacks
    /// can land after the work has finished and the assertion below would be a
    /// race. Marshalling to the UI thread is the shell's business; what is under
    /// test here is what the handler says and when it says it.
    /// </remarks>
    private sealed class Reports : IProgress<PhotoRemovalProgress>
    {
        private readonly Action<PhotoRemovalProgress>? _also;

        public Reports(Action<PhotoRemovalProgress>? also = null) => _also = also;

        public List<PhotoRemovalProgress> Seen { get; } = [];

        public void Report(PhotoRemovalProgress value)
        {
            Seen.Add(value);
            _also?.Invoke(value);
        }
    }

    /// <summary>
    /// Deleting a photograph takes everything the models worked out about it.
    /// </summary>
    /// <remarks>
    /// Both indexes hang off the asset by a cascading foreign key, but the row
    /// is removed with ExecuteDelete, which is raw SQL - so the cascade is the
    /// database's to honour rather than EF's, and SQLite only honours one when
    /// foreign keys are enforced on the connection. If that were ever off this
    /// would not fail loudly; it would leave a vector behind, and a search would
    /// go on offering a photograph that no longer exists.
    /// </remarks>
    [Fact]
    public async Task Handle_TakesTheFacesAndTheDescriptionWithThePhotograph()
    {
        string rendition = await SaveRenditionAsync("gone");
        int assetId = AddPhoto(@"20230201\a.jpg", rendition);
        int faceId = AddFace(assetId);
        NameSomebodyOn(faceId);
        DescribeIt(assetId, rendition);

        Assert.Equal(1, await _db.Faces.CountAsync());
        Assert.Equal(1, await _db.PhotoContent.CountAsync());
        Assert.Equal(1, await _db.FaceAssignments.CountAsync());

        Assert.True(await RemoveAsync(assetId));

        Assert.Equal(0, await _db.Assets.CountAsync());
        Assert.Equal(0, await _db.Faces.CountAsync());
        Assert.Equal(0, await _db.PhotoContent.CountAsync());
        Assert.Equal(0, await _db.FaceAssignments.CountAsync());

        // The person survives - they are not a fact about this photograph.
        Assert.Equal(1, await _db.Set<Person>().CountAsync());
    }

    /// <summary>
    /// One photograph through the entry point the app actually uses.
    /// </summary>
    /// <remarks>
    /// There is no single-photo overload any more: every gesture that deletes
    /// goes through the batch so that all of them get the same progress screen,
    /// and a test that took a shortcut past it would be testing a path nothing
    /// runs.
    /// </remarks>
    private async Task<bool> RemoveAsync(int assetId) =>
        (await _handler.HandleAsync([assetId])).Deleted == 1;

    private void DescribeIt(int assetId, string thumbnailName)
    {
        float[] values = new float[ContentEmbedding.Dimensions];
        values[0] = 1f;

        _db.PhotoContent.Add(new PhotoContent
        {
            AssetId = assetId,
            ThumbnailName = thumbnailName,
            Vector = new ContentEmbedding(values),
            IndexedUtc = new DateTime(2026, 8, 16),
        });

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private async Task<string> SaveRenditionAsync(string seed) =>
        await _thumbnails.SaveAsync(new GeneratedThumbnail(
            [1, 2, 3], [4, 5, 6], 800, 600, null, new PerceptualHash(0), seed.PadRight(32, '0')));

    private int AddPhoto(string relativePath, string? thumbnailName, int photoSourceId = 1)
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
            ThumbnailName = thumbnailName,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return asset.Id;
    }

    private int AddFace(int assetId)
    {
        var face = new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(10, 10, 40, 40),
            DetectScore = 0.9f,
            Embedding = TestEmbeddings.At(0),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return face.Id;
    }

    private void NameSomebodyOn(int faceId)
    {
        var person = new Person { DisplayName = "Ana Lim" };
        _db.Set<Person>().Add(person);
        _db.SaveChanges();

        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = faceId, PersonId = person.Id, Source = AssignmentSource.Confirmed,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Sources that are up, and sources that are away.
    /// </summary>
    /// <remarks>
    /// Counts what it was asked, because asking once per source rather than once
    /// per photograph is the difference between a batch refusing in a moment and
    /// one that blocks for 21 seconds a file.
    /// </remarks>
    private sealed class FakeAvailability : ISourceAvailability
    {
        public HashSet<string> Away { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Asked { get; } = [];

        public bool CanReach(string sourceRoot)
        {
            Asked.Add(sourceRoot);
            return !Away.Contains(sourceRoot);
        }
    }

    private sealed class FakeOriginal : IOriginalFile
    {
        public bool Recycles { get; set; }

        public bool Refuses { get; set; }

        /// <summary>A file somebody removed by hand since the row was written.</summary>
        public bool AlreadyGone { get; set; }

        public List<string> Deleted { get; } = [];

        public bool GoesToRecycleBin(string fullPath) => Recycles;

        public bool Delete(string fullPath)
        {
            if (Refuses)
            {
                return false;
            }

            if (!AlreadyGone)
            {
                Deleted.Add(fullPath);
            }

            return true;
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
