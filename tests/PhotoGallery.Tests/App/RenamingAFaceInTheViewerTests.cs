using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Choosing somebody else from the viewer's list for a face that is already named.
/// </summary>
/// <remarks>
/// The list opens on any face, named ones included, with the current name
/// marked - which reads as "change who this is". It added the new name beside
/// the old one instead, so the face stayed on the first person's page and the
/// box could flip back to the old name when the picture was read again.
///
/// <para>Against the real service graph and a real file, because what is wrong
/// is what the database holds afterwards.</para>
/// </remarks>
public sealed class RenamingAFaceInTheViewerTests : IDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly GalleryViewModel _gallery;

    public RenamingAFaceInTheViewerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-rename-face-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _services = new ServiceCollection()
            .AddPhotoGalleryInfrastructure(_root)
            .AddPhotoGalleryHandlers()
            .BuildServiceProvider();

        using (IServiceScope scope = _services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<GalleryDbContext>().Database.Migrate();
        }

        Seed();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IThumbnailStore>());

        _gallery.SetColumns(3);
    }

    [Fact]
    public async Task ChoosingSomebodyElse_MovesTheFaceToThem()
    {
        await _gallery.ShowWithoutNamedFacesCommand.ExecuteAsync(null);
        GalleryTile group = Assert.Single(Wall());

        _gallery.ShowFaceNames = true;
        _gallery.OpenPhotoCommand.Execute(group);
        await TestWait.ForAsync(
            () => _gallery.OpenFaces.Count == 2,
            "both faces to be drawn",
            attempts: 300);

        int left = _gallery.OpenFaces[0].FaceId;
        await NameAsync(left, typed: "Ana");
        await NameAsync(_gallery.OpenFaces[1].FaceId, typed: "Ben");
        Assert.Equal(1, await PhotosOfAsync("Ana"));

        // Now the left face turns out to be Ben too, chosen from the list.
        BeginNaming(left);
        PersonChoiceItem ben = _gallery.Picker.Choices.Single(choice => choice.DisplayName == "Ben");
        await _gallery.Picker.ChooseCommand.ExecuteAsync(ben);

        await TestWait.ForAsync(
            () => _gallery.OpenFaces.Count == 2
                && _gallery.OpenFaces.All(face => face.Label == "Ben"),
            "both faces to read Ben after the picture is read again",
            attempts: 300);

        Assert.Equal(["Ben"], await ConfirmedNamesOfAsync(left));
        Assert.Equal(0, await PhotosOfAsync("Ana"));
        Assert.Equal(1, await PhotosOfAsync("Ben"));
    }

    // ------------------------------------------------------------- helpers

    private List<GalleryTile> Wall() => [.. _gallery.Rows.SelectMany(row => row.Tiles)];

    private void BeginNaming(int faceId) =>
        _gallery.BeginNamingFaceCommand.Execute(
            _gallery.OpenFaces.Single(face => face.FaceId == faceId));

    private async Task NameAsync(int faceId, string typed)
    {
        BeginNaming(faceId);
        _gallery.Picker.Typed = typed;
        await _gallery.Picker.AddCommand.ExecuteAsync(null);
        await TestWait.ForAsync(
            () => _gallery.OpenFaces.Any(face => face.FaceId == faceId && face.Label == typed),
            $"the face to read {typed}",
            attempts: 300);
    }

    /// <summary>The photograph count the people list shows beside one name.</summary>
    private async Task<int> PhotosOfAsync(string displayName)
    {
        using IServiceScope scope = _services.CreateScope();
        IReadOnlyList<PersonDirectoryEntry> everyone = await scope.ServiceProvider
            .GetRequiredService<IPeopleReader>()
            .GetDirectoryAsync();

        return everyone.Single(person => person.DisplayName == displayName).Photos;
    }

    private async Task<List<string>> ConfirmedNamesOfAsync(int faceId)
    {
        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();

        return await db.FaceAssignments
            .Where(assignment => assignment.FaceId == faceId
                              && assignment.Source == AssignmentSource.Confirmed)
            .Join(
                db.Set<Person>(),
                assignment => assignment.PersonId,
                person => person.Id,
                (assignment, person) => person.DisplayName)
            .OrderBy(name => name)
            .ToListAsync();
    }

    private void Seed()
    {
        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();

        db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        db.Assets.Add(new Asset
        {
            Id = 1,
            PhotoSourceId = 1,
            RelativePath = @"2020\group.jpg",
            Length = 1,
            ModifiedUtc = new DateTime(2020, 1, 1),
            CreatedUtc = new DateTime(2020, 1, 1),
            IndexedUtc = new DateTime(2020, 1, 1),
            Kind = AssetKind.Photo,
        });
        db.SaveChanges();

        // Two faces far enough apart that neither is proposed as the other.
        foreach (double degrees in new[] { 0d, 90d })
        {
            db.Faces.Add(new Face
            {
                AssetId = 1,
                Bounds = new FaceBounds(10 + (int)degrees, 10, 40, 40),
                DetectScore = 0.9f,
                Embedding = TestEmbeddings.At(degrees),
            });
        }

        db.SaveChanges();
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder; SQLite's pooled handle may outlive the provider.
        }
    }
}
