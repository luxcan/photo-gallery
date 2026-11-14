using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Sharing;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// The measuring step inside the duplicates pass and the suggestion the screen
/// shows from it (PRP 14), against a real index and scripted previews.
/// </summary>
/// <remarks>
/// The previews are scripted rather than drawn: what is under test is which
/// photographs get read, what is kept, and which copy comes out suggested for
/// which reason. The meter itself has its own tests on real pixels.
/// </remarks>
public sealed class DuplicateSuggestionTests : IDisposable
{
    private static readonly DateTime s_shot = new(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly SqliteDuplicateRepository _duplicates;
    private readonly SqlitePhotoQualityRepository _quality;
    private readonly FileSystemThumbnailStore _thumbnails;
    private readonly ScriptedPreviews _previews = new();
    private readonly ScriptedEyes _eyes = new();
    private readonly StubModelStore _models = new StubModelStore().Running(ModelId.FaceLandmarks, "landmarks-v1");

    public DuplicateSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-suggest-{Guid.NewGuid():N}");
        var workingFolder = new WorkingFolder(Path.Combine(_root, "working"));
        workingFolder.EnsureCreated();
        _thumbnails = new FileSystemThumbnailStore(workingFolder);

        _db = new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
            .Options);
        _db.Database.Migrate();
        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = Path.Combine(_root, "library") });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _duplicates = new SqliteDuplicateRepository(_db);
        _quality = new SqlitePhotoQualityRepository(_db);
    }

    [Fact]
    public async Task TheSharperFrameIsSuggestedAndTheOtherSaysBlurred()
    {
        int sharp = AddPhoto("a", phash: 0b1111, sharpness: 400);
        int shaken = AddPhoto("b", phash: 0b1111, sharpness: 100);

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Equal(sharp, set.Keeper!.AssetId);
        Assert.Equal(KeeperCriterion.Sharpness, set.DecidedBy);
        Assert.Equal(KeeperCriterion.Sharpness, set.Copies.Single(copy => copy.AssetId == shaken).DecidedBy);
    }

    [Fact]
    public async Task ASetWithNoSuggestionStaysOnScreen()
    {
        AddPhoto("a", phash: 0b1111, sharpness: 300);
        AddPhoto("b", phash: 0b1111, sharpness: 290);

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Null(set.Keeper);
        Assert.Null(set.DecidedBy);
        Assert.Equal(2, set.Copies.Count);
        Assert.Equal(1, (await new SqliteLibraryIndex(_db).GetCountsAsync()).UnresolvedDuplicateSets);
    }

    [Fact]
    public async Task IdenticalSetsAreNeverMeasuredAndNeverSuggested()
    {
        AddPhoto("a", hash: "same", sharpness: 900);
        AddPhoto("b", hash: "same", sharpness: 100);

        DuplicateBoard board = await FindAndShowBoardAsync();

        Assert.Empty(_previews.Read);
        DuplicateSetView set = Assert.Single(board.Exact);
        Assert.Null(set.Keeper);
        Assert.Empty(await _db.PreviewQuality.ToListAsync());
    }

    [Fact]
    public async Task OnlyCopiesThatLookTheSameAreMeasured()
    {
        int a = AddPhoto("a", phash: 0b1111);
        int b = AddPhoto("b", phash: 0b1111);
        AddPhoto("alone", phash: 0xFFFF_FFFF_0000_0000);

        await FindAsync();

        Assert.Equal(
            [Preview(a), Preview(b)],
            _previews.Read.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task APhotographMeasuredOnceIsNotReadAgain()
    {
        AddPhoto("a", phash: 0b1111);
        AddPhoto("b", phash: 0b1111);

        await FindAsync();
        await FindAsync();

        Assert.Equal(2, _previews.Read.Count);
    }

    [Fact]
    public async Task APreviewWrittenAgainIsMeasuredAgain()
    {
        int a = AddPhoto("a", phash: 0b1111);
        AddPhoto("b", phash: 0b1111);
        await FindAsync();

        File.SetLastWriteTimeUtc(Preview(a), DateTime.UtcNow.AddMinutes(5));
        await FindAsync();

        Assert.Equal(2, _previews.Read.Count(path => path == Preview(a)));
    }

    [Fact]
    public async Task EyesThatCouldNotBeReadAreReadAgainOnTheNextPass()
    {
        // The graphs failing on a preview is not a face that could not be told:
        // nothing is kept for its eyes, so the next pass asks again.
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 900);
        int open = AddPhoto("b", phash: 0b1111, sharpness: 100);
        Name(AddFace(blink), "Mum");
        Name(AddFace(open), "Mum");
        _eyes.Closed.Add(Preview(blink));
        _eyes.FailingOnce.Add(Preview(blink));

        await FindAsync();
        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Equal(2, _eyes.Asked.Count(path => path == Preview(blink)));
        Assert.Equal(KeeperCriterion.Eyes, set.DecidedBy);
        Assert.Equal(open, set.Keeper!.AssetId);
    }

    [Fact]
    public async Task NothingLeftToMeasureNamesNoMeasuringStep()
    {
        // The overlay is told the measuring step only when there is something
        // to measure; otherwise it would show that title over the grouping.
        AddPhoto("a", phash: 0b1111);
        AddPhoto("b", phash: 0b1111);
        await FindAsync();

        var reports = new RecordedProgress();
        await FindAsync(progress: reports);

        Assert.DoesNotContain(reports.Phases, phase => phase == DuplicateProgress.Measuring);
    }

    [Fact]
    public async Task StoppingKeepsWhatWasMeasuredAndLeavesTheSetsAlone()
    {
        AddPhoto("a", phash: 0b1111);
        AddPhoto("b", phash: 0b1111);
        using var stop = new CancellationTokenSource();
        _previews.AfterRead = stop.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FindAsync(stop.Token));

        Assert.Single(await _db.PreviewQuality.ToListAsync());
        Assert.Empty(await _db.DuplicateSets.ToListAsync());

        _previews.AfterRead = null;
        await FindAsync();

        // Carried on: the photograph measured before the Stop was not read again.
        Assert.Equal(2, await _db.PreviewQuality.CountAsync());
        Assert.Equal(2, _previews.Read.Count);
    }

    [Fact]
    public async Task TheOpenFrameWinsOverASharperBlinkAndTheCardNamesWho()
    {
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 900, width: 4000);
        int open = AddPhoto("b", phash: 0b1111, sharpness: 100, width: 1000);
        int blinkFace = Name(AddFace(blink), "Mum");
        Name(AddFace(open), "Mum");
        _eyes.Closed.Add(Preview(blink));

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Equal(open, set.Keeper!.AssetId);
        Assert.Equal(KeeperCriterion.Eyes, set.DecidedBy);
        DuplicateCopy lost = set.Copies.Single(copy => copy.AssetId == blink);
        Assert.Equal(["Mum"], lost.ClosedNames);
        Assert.Equal(blinkFace, Assert.Single(lost.ClosedFaces!).FaceId);
        Assert.Equal(1024, lost.PreviewWidth);
    }

    [Fact]
    public async Task TurningAMeasuredCopyTurnsTheSpaceItsMarkersArePlacedIn()
    {
        // A turn moves the face boxes into the turned preview at once, so the
        // size they are drawn against must turn with them - not wait for the
        // next look for duplicates.
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 900);
        int open = AddPhoto("b", phash: 0b1111, sharpness: 100);
        Name(AddFace(blink), "Mum");
        Name(AddFace(open), "Mum");
        _eyes.Closed.Add(Preview(blink));
        await FindAsync();

        await new SqliteFaceRepository(_db).TurnFacesAsync([blink], 90, 1024, 768);
        DuplicateSetView set = Assert.Single((await ShowAsync()).Near);

        DuplicateCopy lost = set.Copies.Single(copy => copy.AssetId == blink);
        Assert.Equal((768, 1024), (lost.PreviewWidth, lost.PreviewHeight));
        FaceBounds marker = Assert.Single(lost.ClosedFaces!).Bounds;
        Assert.True(marker.X + marker.Width <= lost.PreviewWidth && marker.Y + marker.Height <= lost.PreviewHeight);
    }

    [Fact]
    public async Task AMeasurementOfAPreviewSinceReplacedIsNotTrusted()
    {
        // Prepared again under a new name, the photograph has a picture its
        // readings were never taken from; until it is measured again it has none.
        int sharp = AddPhoto("a", phash: 0b1111, sharpness: 400);
        AddPhoto("b", phash: 0b1111, sharpness: 100);
        await FindAsync();

        await _db.Assets.Where(asset => asset.Id == sharp)
            .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.ThumbnailName, "a-again.jpg"));
        DuplicateSetView set = Assert.Single((await ShowAsync()).Near);

        Assert.Null(set.Keeper);
    }

    [Fact]
    public async Task ASetWhoseStoredKeeperWasDeletedIsStillAskedAbout()
    {
        // The suggestion is worked out on the screen, so the copy the last pass
        // stored as keeper is not what the screen offered. The two left are
        // still a question, and the screen and the count both say so.
        int stored = AddPhoto("a", phash: 0b1111, sharpness: 900);
        AddPhoto("b", phash: 0b1111, sharpness: 100);
        AddPhoto("c", phash: 0b1111, sharpness: 100);
        await FindAsync();

        await new SqliteAssetRepository(_db).RemoveAsync([stored]);
        DuplicateSetView set = Assert.Single((await ShowAsync()).Near);

        Assert.Equal(2, set.Copies.Count);
        Assert.Equal(1, (await new SqliteLibraryIndex(_db).GetCountsAsync()).UnresolvedDuplicateSets);
    }

    [Fact]
    public async Task DistancesAreMeasuredFromTheCopySuggestedNow()
    {
        // The pass suggested the sharp blink; Mum is named afterwards, and the
        // screen now suggests the open frame. "Bits differ" is how far each
        // copy is from that one, not from the copy the pass chose.
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 900);
        int open = AddPhoto("b", phash: 0b0111, sharpness: 100);
        int other = AddPhoto("c", phash: 0b0011, sharpness: 100, width: 3000);
        int blinkFace = AddFace(blink);
        int openFace = AddFace(open);
        _eyes.Closed.Add(Preview(blink));
        await FindAsync();

        Name(blinkFace, "Mum");
        Name(openFace, "Mum");
        DuplicateSetView set = Assert.Single((await ShowAsync()).Near);

        Assert.Equal(open, set.Keeper!.AssetId);
        Assert.Equal(0, set.Keeper.Distance);
        Assert.Equal(1, set.Copies.Single(copy => copy.AssetId == blink).Distance);
        Assert.Equal(1, set.Copies.Single(copy => copy.AssetId == other).Distance);
    }

    [Theory]
    [InlineData("stranger")]
    [InlineData("rejected")]
    [InlineData("cleared")]
    [InlineData("ignored")]
    [InlineData("unnamed")]
    [InlineData("deleted person")]
    public async Task OnlyKnownPeoplesEyesDecide(string who)
    {
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 300);
        int open = AddPhoto("b", phash: 0b1111, sharpness: 100);
        int closedFace = AddFace(blink);
        Name(AddFace(open), "Mum");
        _eyes.Closed.Add(Preview(blink));

        switch (who)
        {
            case "stranger":
                break;
            case "rejected":
                Name(closedFace, "Mum", AssignmentSource.Rejected);
                break;
            case "cleared":
                Name(closedFace, "Mum", AssignmentSource.Cleared);
                break;
            case "ignored":
                Name(closedFace, "Mum");
                Ignore(closedFace);
                break;
            case "unnamed":
                Name(closedFace, " ");
                break;
            case "deleted person":
                Name(closedFace, "Gone");
                await _db.People.Where(person => person.DisplayName == "Gone")
                    .ExecuteUpdateAsync(set => set.SetProperty(person => person.DeletedUtc, s_shot));
                break;
        }

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        // Sharpness decides instead, for the copy whose face the rule ignores.
        Assert.Equal(KeeperCriterion.Sharpness, set.DecidedBy);
        Assert.Equal(blink, set.Keeper!.AssetId);
    }

    [Fact]
    public async Task AProposedMatchCountsAsKnown()
    {
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 300);
        int open = AddPhoto("b", phash: 0b1111, sharpness: 100);
        Name(AddFace(blink), "Mum", AssignmentSource.Proposed);
        Name(AddFace(open), "Mum", AssignmentSource.Proposed);
        _eyes.Closed.Add(Preview(blink));

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Equal(KeeperCriterion.Eyes, set.DecidedBy);
        Assert.Equal(open, set.Keeper!.AssetId);
    }

    [Fact]
    public async Task WithoutTheEyeModelEyesAreOffAndTheRestOfTheLadderRuns()
    {
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 900);
        int open = AddPhoto("b", phash: 0b1111, sharpness: 100);
        Name(AddFace(blink), "Mum");
        Name(AddFace(open), "Mum");
        _eyes.Closed.Add(Preview(blink));
        _models.Without(ModelId.FaceLandmarks);

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Empty(_eyes.Asked);
        Assert.Equal(KeeperCriterion.Sharpness, set.DecidedBy);
        Assert.Equal(blink, set.Keeper!.AssetId);
        Assert.All(set.Copies, copy => Assert.Empty(copy.ClosedFaces ?? []));
        Assert.All(await _db.FaceEyes.ToListAsync(), eyes => Assert.Null(eyes.State));
    }

    [Fact]
    public async Task EyesReadBeforeTheModelWentAreNotTrusted()
    {
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 900);
        int open = AddPhoto("b", phash: 0b1111, sharpness: 100);
        Name(AddFace(blink), "Mum");
        Name(AddFace(open), "Mum");
        _eyes.Closed.Add(Preview(blink));
        await FindAsync();

        _models.Without(ModelId.FaceLandmarks);
        DuplicateSetView set = Assert.Single((await ShowAsync()).Near);

        Assert.Equal(KeeperCriterion.Sharpness, set.DecidedBy);
    }

    [Fact]
    public async Task FacesMeasuredWithoutTheModelAreMeasuredAgainOnceItArrives()
    {
        int blink = AddPhoto("a", phash: 0b1111, sharpness: 900);
        int open = AddPhoto("b", phash: 0b1111, sharpness: 100);
        Name(AddFace(blink), "Mum");
        Name(AddFace(open), "Mum");
        _eyes.Closed.Add(Preview(blink));
        _models.Without(ModelId.FaceLandmarks);
        await FindAsync();

        _models.Running(ModelId.FaceLandmarks, "landmarks-v1");
        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Equal(KeeperCriterion.Eyes, set.DecidedBy);
        Assert.Equal(open, set.Keeper!.AssetId);
    }

    [Fact]
    public async Task AMeasurementGoesWithItsFace()
    {
        int a = AddPhoto("a", phash: 0b1111);
        AddPhoto("b", phash: 0b1111);
        Name(AddFace(a), "Mum");
        await FindAsync();
        Assert.Single(await _db.FaceEyes.ToListAsync());

        await _db.Faces.ExecuteDeleteAsync();

        Assert.Empty(await _db.FaceEyes.ToListAsync());
    }

    [Fact]
    public async Task AReSavedSmallerCopyLosesToTheOriginalButNotOnSharpness()
    {
        // Its preview is below the common size, so it has no sharpness to lose
        // on; the original wins on its camera details instead.
        int original = AddPhoto("a", phash: 0b1111, sharpness: 300, taken: s_shot, width: 4000);
        int resaved = AddPhoto("b", phash: 0b1111, sharpness: null, width: 600);

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Equal(original, set.Keeper!.AssetId);
        Assert.Equal(KeeperCriterion.Original, set.Copies.Single(copy => copy.AssetId == resaved).DecidedBy);
    }

    [Fact]
    public async Task CopiesThatDifferOnlyInBytesAreLeftToThePerson()
    {
        // Burst frames share a capture time to the second, so bytes cannot tell
        // them from one photograph saved twice - and the set stays on screen.
        AddPhoto("a", phash: 0b1111, taken: s_shot, length: 2000);
        AddPhoto("b", phash: 0b1111, taken: s_shot, length: 1000);

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Null(set.Keeper);
        Assert.All(set.Copies, copy => Assert.Equal(DuplicateRole.Undecided, copy.Role));
    }

    [Fact]
    public async Task BytesSayNothingBetweenTwoFramesOfABurst()
    {
        AddPhoto("a", phash: 0b1111, taken: s_shot, length: 2000);
        AddPhoto("b", phash: 0b1111, taken: s_shot.AddSeconds(1), length: 1000);

        DuplicateSetView set = Assert.Single(await FindAndShowAsync());

        Assert.Null(set.Keeper);
    }

    private async Task<IReadOnlyList<DuplicateSetView>> FindAndShowAsync() =>
        (await FindAndShowBoardAsync()).Near;

    private async Task<DuplicateBoard> FindAndShowBoardAsync()
    {
        await FindAsync();
        return await ShowAsync();
    }

    private Task<DuplicateScan> FindAsync(
        CancellationToken cancellationToken = default, IProgress<DuplicateProgress>? progress = null) =>
        new FindDuplicatesHandler(
                _duplicates,
                _quality,
                new CopyMeasurer(_quality, _previews, _thumbnails, _eyes),
                _models)
            .HandleAsync(progress: progress, cancellationToken: cancellationToken);

    private Task<DuplicateBoard> ShowAsync() =>
        new GetDuplicatesHandler(_duplicates, _quality, _models).HandleAsync();

    private string Preview(int assetId) =>
        _thumbnails.ResolvePreviewPath(_db.Assets.AsNoTracking().Single(asset => asset.Id == assetId).ThumbnailName!);

    private int AddPhoto(
        string name,
        string? hash = null,
        ulong phash = 0,
        double? sharpness = 200,
        int width = 4000,
        DateTime? taken = null,
        long length = 1000)
    {
        string thumbnail = $"{name}.jpg";
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = $@"photos\{name}.jpg",
            Length = length,
            ModifiedUtc = s_shot,
            CreatedUtc = s_shot,
            IndexedUtc = s_shot,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ContentHash = hash ?? name,
            PerceptualHash = new PerceptualHash(phash),
            ThumbnailName = thumbnail,
            Width = width,
            Height = width * 3 / 4,
            TakenUtc = taken,
        };

        _db.Assets.Add(asset);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        string preview = _thumbnails.ResolvePreviewPath(thumbnail);
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        File.WriteAllBytes(preview, [1, 2, 3]);
        _previews.Script[preview] = sharpness;

        return asset.Id;
    }

    private int AddFace(int assetId)
    {
        var face = new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(100, 100, 200, 200),
            DetectScore = 0.9f,
            Embedding = TestEmbeddings.At(assetId),
        };

        _db.Faces.Add(face);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return face.Id;
    }

    private int Name(int faceId, string person, AssignmentSource source = AssignmentSource.Confirmed)
    {
        Person? named = _db.People.FirstOrDefault(row => row.DisplayName == person);
        if (named is null)
        {
            named = new Person { DisplayName = person };
            _db.People.Add(named);
            _db.SaveChanges();
        }

        _db.FaceAssignments.Add(new FaceAssignment
        {
            FaceId = faceId,
            PersonId = named.Id,
            Source = source,
            DecidedUtc = s_shot,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return faceId;
    }

    private void Ignore(int faceId)
    {
        _db.Faces.Where(face => face.Id == faceId)
            .ExecuteUpdate(set => set.SetProperty(face => face.IgnoredUtc, s_shot));
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

    /// <summary>
    /// Previews whose readings are given rather than drawn: a scene sharpness
    /// per preview (null for one below the common size), every face read as
    /// sharp as the scene, nothing clipped.
    /// </summary>
    private sealed class ScriptedPreviews : IPreviewQualityReader
    {
        public Dictionary<string, double?> Script { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Read { get; } = [];

        public Action? AfterRead { get; set; }

        PreviewReading? IPreviewQualityReader.Read(string previewPath, IReadOnlyList<FaceBounds> faces)
        {
            Read.Add(previewPath);
            AfterRead?.Invoke();

            double? sharpness = Script[previewPath];
            return new PreviewReading(1024, 768, sharpness, 0.01, 0.01, [.. faces.Select(_ => sharpness)]);
        }
    }

    /// <summary>Every phase reported, in order, as it is reported.</summary>
    private sealed class RecordedProgress : IProgress<DuplicateProgress>
    {
        public List<string> Phases { get; } = [];

        public void Report(DuplicateProgress value) => Phases.Add(value.Phase);
    }

    /// <summary>
    /// Eyes open on every preview except the ones listed as blinking, and no
    /// answer at all the first time for the ones listed as failing - as the
    /// graphs give when they will not run.
    /// </summary>
    private sealed class ScriptedEyes : IEyeMeasurer
    {
        public HashSet<string> Closed { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FailingOnce { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<EyeMeasurement>?> MeasureAsync(
            string previewPath, IReadOnlyList<FaceBounds> faces, CancellationToken cancellationToken = default)
        {
            Asked.Add(previewPath);
            if (FailingOnce.Remove(previewPath))
            {
                return Task.FromResult<IReadOnlyList<EyeMeasurement>?>(null);
            }

            double openness = Closed.Contains(previewPath) ? 0.02 : 0.30;
            return Task.FromResult<IReadOnlyList<EyeMeasurement>?>(
                [.. faces.Select(_ => new EyeMeasurement(openness, openness, 0.05, 0.95))]);
        }
    }
}
