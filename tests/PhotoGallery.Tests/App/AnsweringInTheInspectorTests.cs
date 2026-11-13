using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
using PhotoGallery.App.People;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Answering a person's queue one face at a time, when the face is confirmed as
/// somebody else as well.
/// </summary>
/// <remarks>
/// The state an older build left behind, and that another laptop can still
/// hold until it merges: a face confirmed as Bea and proposed for Ana. Keeping
/// it for Ana - or giving it to a third person - takes it off Bea, and Bea's
/// eras were built with it among her examples. The answers were written
/// straight to the index, which takes the confirmation away but cannot know
/// whose eras that leaves wrong, so Bea went on learning from a face the user
/// had just said was not hers.
///
/// <para>Against the real service graph, because what is wrong is what the
/// database holds afterwards.</para>
/// </remarks>
public sealed class AnsweringInTheInspectorTests : IDisposable
{
    private static readonly DateTime s_taken = new(2019, 5, 4, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly PeopleViewModel _people;

    private int _ana;
    private int _bea;
    private int _doubled;
    private int _next;

    public AnsweringInTheInspectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-inspector-answers-{Guid.NewGuid():N}");
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

        _people = new PeopleViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _services.GetRequiredService<IThumbnailStore>());
    }

    [Fact]
    public async Task KeepingAFaceConfirmedAsSomebodyElse_RebuildsWhoItWasTakenFrom()
    {
        Assert.Equal(2, await SamplesOfAsync(_bea));

        await InspectTheDoubledFaceAsync();
        await _people.KeepInspectedCommand.ExecuteAsync(null);

        Assert.Equal([_doubled], (await ConfirmedAsync(_ana)).Where(face => face == _doubled));
        Assert.DoesNotContain(_doubled, await ConfirmedAsync(_bea));
        Assert.Equal(1, await SamplesOfAsync(_bea));
    }

    [Fact]
    public async Task GivingAFaceConfirmedAsSomebodyElseToAThirdPerson_RebuildsWhoItWasTakenFrom()
    {
        Assert.Equal(2, await SamplesOfAsync(_bea));

        await InspectTheDoubledFaceAsync();
        _people.ReassignInspectedCommand.Execute(null);
        _people.Reassign.Typed = "Cy";
        await _people.Reassign.AddCommand.ExecuteAsync(null);

        Assert.DoesNotContain(_doubled, await ConfirmedAsync(_bea));
        Assert.Equal(1, await SamplesOfAsync(_bea));
    }

    [Fact]
    public async Task WhileAnAnswerIsBeingWritten_NoOtherAnswerOrStepCanStart()
    {
        // The answer is written off the UI thread, and until it is the face it
        // is about is still the one open. A second click in that window - Leave
        // out straight after Keep, meant for the next face - landed on the same
        // face again: it overwrote the first answer, counted twice, and sent the
        // queue back to its start.
        await InspectTheDoubledFaceAsync();

        Task keep = _people.KeepInspectedCommand.ExecuteAsync(null);

        Assert.False(_people.DropInspectedCommand.CanExecute(null));
        Assert.False(_people.ReassignInspectedCommand.CanExecute(null));
        Assert.False(_people.InspectNextCommand.CanExecute(null));
        Assert.False(_people.InspectPreviousCommand.CanExecute(null));

        // The arrow keys call Execute without asking CanExecute first.
        Task drop = _people.DropInspectedCommand.ExecuteAsync(null);
        await Task.WhenAll(keep, drop);

        Assert.Contains(_doubled, await ConfirmedAsync(_ana));
        Assert.Equal((1, 0), (_people.Kept, _people.LeftOut));
        Assert.Equal(_next, _people.Inspected?.FaceId);
        Assert.True(_people.DropInspectedCommand.CanExecute(null));
    }

    /// <summary>
    /// Opens Ana's queue on the face that is Bea's as well, with another
    /// proposal waiting behind it.
    /// </summary>
    private async Task InspectTheDoubledFaceAsync()
    {
        _people.SelectedPerson = new PersonItem(new PersonSummary(_ana, "Ana", 1, 1, 1, null));
        _people.Proposals.Add(new FaceCropItem(new FaceThumbnail(
            _doubled, 1, "thumb-1.jpg", new FaceBounds(0, 0, 80, 80), s_taken,
            @"2019\1.jpg", Path.Combine(_root, "2019", "1.jpg"))));
        _people.Proposals.Add(new FaceCropItem(new FaceThumbnail(
            _next, 4, "thumb-4.jpg", new FaceBounds(0, 0, 80, 80), s_taken,
            @"2019\4.jpg", Path.Combine(_root, "2019", "4.jpg"))));

        await _people.InspectFaceCommand.ExecuteAsync(null);
        Assert.Equal(_doubled, _people.Inspected?.FaceId);
    }

    /// <summary>
    /// Bea confirmed in two photographs, her eras built from both; the first of
    /// them proposed for Ana as well; Ana confirmed in a third and proposed in a
    /// fourth.
    /// </summary>
    private void Seed()
    {
        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();
        db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });

        var ana = new Person { DisplayName = "Ana" };
        var bea = new Person { DisplayName = "Bea" };
        db.Set<Person>().AddRange(ana, bea);
        db.SaveChanges();
        (_ana, _bea) = (ana.Id, bea.Id);

        int[] faces = [.. Enumerable.Range(1, 4).Select(id => AddFace(db, id))];
        _doubled = faces[0];
        _next = faces[3];

        Assign(db, faces[0], _bea, AssignmentSource.Confirmed);
        Assign(db, faces[1], _bea, AssignmentSource.Confirmed);
        Assign(db, faces[0], _ana, AssignmentSource.Proposed);
        Assign(db, faces[2], _ana, AssignmentSource.Confirmed);
        Assign(db, faces[3], _ana, AssignmentSource.Proposed);
        db.SaveChanges();

        AssignFacesHandler handler = scope.ServiceProvider.GetRequiredService<AssignFacesHandler>();
        handler.RebuildErasAsync(_bea).GetAwaiter().GetResult();
        handler.RebuildErasAsync(_ana).GetAwaiter().GetResult();
    }

    private static int AddFace(GalleryDbContext db, int assetId)
    {
        db.Assets.Add(new Asset
        {
            Id = assetId,
            PhotoSourceId = 1,
            RelativePath = $@"2019\{assetId}.jpg",
            Length = 1,
            ModifiedUtc = s_taken,
            CreatedUtc = s_taken,
            IndexedUtc = s_taken,
            TakenUtc = s_taken.AddDays(assetId),
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ThumbnailName = $"thumb-{assetId}.jpg",
        });

        var face = new Face
        {
            AssetId = assetId,
            Bounds = new FaceBounds(0, 0, 80, 80),
            DetectScore = 0.9f,
            Embedding = TestEmbeddings.At(assetId * 2),
        };
        db.Faces.Add(face);
        db.SaveChanges();

        return face.Id;
    }

    private static void Assign(GalleryDbContext db, int faceId, int personId, AssignmentSource source) =>
        db.Set<FaceAssignment>().Add(new FaceAssignment
        {
            FaceId = faceId,
            PersonId = personId,
            Source = source,
            DecidedUtc = s_taken,
        });

    private async Task<int[]> ConfirmedAsync(int personId)
    {
        using IServiceScope scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GalleryDbContext>()
            .Set<FaceAssignment>()
            .Where(row => row.PersonId == personId && row.Source == AssignmentSource.Confirmed)
            .Select(row => row.FaceId)
            .OrderBy(id => id)
            .ToArrayAsync();
    }

    /// <summary>How many confirmed faces one person's eras were built from.</summary>
    private async Task<int> SamplesOfAsync(int personId)
    {
        using IServiceScope scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GalleryDbContext>()
            .Set<PersonEra>()
            .Where(era => era.PersonId == personId)
            .SumAsync(era => era.SampleCount);
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
