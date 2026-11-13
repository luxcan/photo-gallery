using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Confirming a face as somebody when it was already confirmed as somebody else.
/// </summary>
/// <remarks>
/// A face is one person. The index allows one row per face and person, so two
/// confirmations of one face were legal rows - and naming a face from the
/// viewer's list added the second without taking the first away. Every screen
/// that picks one name per face then picked one of the two at random, so a face
/// could not be moved to anybody else.
/// </remarks>
public sealed class FaceReassignmentTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqlitePeopleRepository _repository;

    public FaceReassignmentTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-reassign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _repository = new SqlitePeopleRepository(_db);

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _tempRoot });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ConfirmingAsSomebodyElse_TakesTheFaceOffTheFirstPerson()
    {
        int face = AddFace();
        int ana = await _repository.EnsurePersonAsync("Ana");
        int ben = await _repository.EnsurePersonAsync("Ben");

        await _repository.AssignAsync(ana, [new ScoredFace(face)], AssignmentSource.Confirmed);
        await _repository.AssignAsync(ben, [new ScoredFace(face)], AssignmentSource.Confirmed);

        Assert.Equal([(ben, AssignmentSource.Confirmed)], await RowsFor(face));
    }

    [Fact]
    public async Task ConfirmingAsSomebodyElse_KeepsOtherPeoplesRejections()
    {
        // A rejection is a promise not to ask again, and moving the face between
        // two other people says nothing new about the one who was refused. The
        // guess still waiting for somebody is withdrawn, as it always was.
        int face = AddFace();
        int ana = await _repository.EnsurePersonAsync("Ana");
        int ben = await _repository.EnsurePersonAsync("Ben");
        int chen = await _repository.EnsurePersonAsync("Chen");
        int dev = await _repository.EnsurePersonAsync("Dev");

        await _repository.AssignAsync(chen, [new ScoredFace(face)], AssignmentSource.Rejected);
        await _repository.AssignAsync(dev, [new ScoredFace(face)], AssignmentSource.Proposed);
        await _repository.AssignAsync(ana, [new ScoredFace(face)], AssignmentSource.Confirmed);

        await _repository.AssignAsync(ben, [new ScoredFace(face)], AssignmentSource.Confirmed);

        Assert.Equal(
            [(ben, AssignmentSource.Confirmed), (chen, AssignmentSource.Rejected)],
            await RowsFor(face));
    }

    [Fact]
    public async Task ConfirmingSomebodyWhoWasRefused_ReplacesTheirOwnRefusal()
    {
        // The newer answer about the same person stands - the face cannot be
        // them and not them.
        int face = AddFace();
        int ben = await _repository.EnsurePersonAsync("Ben");

        await _repository.AssignAsync(ben, [new ScoredFace(face)], AssignmentSource.Rejected);
        await _repository.AssignAsync(ben, [new ScoredFace(face)], AssignmentSource.Confirmed);

        Assert.Equal([(ben, AssignmentSource.Confirmed)], await RowsFor(face));
    }

    [Fact]
    public async Task RefusingSomebodyElse_LeavesTheConfirmationAlone()
    {
        // "Not Ben" says nothing against Ana. Only a confirmation settles the
        // face, so only a confirmation takes it off anybody.
        int face = AddFace();
        int ana = await _repository.EnsurePersonAsync("Ana");
        int ben = await _repository.EnsurePersonAsync("Ben");

        await _repository.AssignAsync(ana, [new ScoredFace(face)], AssignmentSource.Confirmed);
        await _repository.AssignAsync(ben, [new ScoredFace(face)], AssignmentSource.Rejected);

        Assert.Equal(
            [(ana, AssignmentSource.Confirmed), (ben, AssignmentSource.Rejected)],
            await RowsFor(face));
    }

    [Fact]
    public async Task ConfirmingAsSomebodyElse_LeavesTheFirstPersonsOtherFaces()
    {
        int moved = AddFace();
        int kept = AddFace();
        int ana = await _repository.EnsurePersonAsync("Ana");
        int ben = await _repository.EnsurePersonAsync("Ben");

        await _repository.AssignAsync(
            ana, [new ScoredFace(moved), new ScoredFace(kept)], AssignmentSource.Confirmed);
        await _repository.AssignAsync(ben, [new ScoredFace(moved)], AssignmentSource.Confirmed);

        Assert.Equal([(ana, AssignmentSource.Confirmed)], await RowsFor(kept));
    }

    /// <summary>Every answer held about one face, by person.</summary>
    private async Task<List<(int PersonId, AssignmentSource Source)>> RowsFor(int faceId)
    {
        var rows = await _db.FaceAssignments
            .AsNoTracking()
            .Where(assignment => assignment.FaceId == faceId)
            .OrderBy(assignment => assignment.PersonId)
            .Select(assignment => new { assignment.PersonId, assignment.Source })
            .ToListAsync();

        return [.. rows.Select(row => (row.PersonId, row.Source))];
    }

    private int AddFace()
    {
        var asset = new Asset
        {
            PhotoSourceId = 1,
            RelativePath = $"2020\\{Guid.NewGuid():N}.jpg",
            Length = 1,
            ModifiedUtc = new DateTime(2020, 1, 1),
            CreatedUtc = new DateTime(2020, 1, 1),
            IndexedUtc = new DateTime(2020, 1, 1),
            Kind = AssetKind.Photo,
        };
        _db.Assets.Add(asset);
        _db.SaveChanges();

        var face = new Face
        {
            AssetId = asset.Id,
            Bounds = new FaceBounds(0, 0, 80, 80),
            DetectScore = 0.9f,
            Embedding = TestEmbeddings.At(0),
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
            // A temporary folder; SQLite's pooled handle may outlive the context.
        }
    }
}
