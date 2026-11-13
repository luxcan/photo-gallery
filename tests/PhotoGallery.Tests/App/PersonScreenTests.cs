using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App;
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
/// One person's page on the People screen: their pictures filtered by kind, the
/// bar that says faces are waiting, and the name and year changed from the ⋯
/// menu.
/// </summary>
/// <remarks>
/// Against the real service graph and a real file, because what the grid shows
/// is the gallery query's answer, and a fake reader would only test that it was
/// asked.
/// </remarks>
public sealed class PersonScreenTests : IDisposable
{
    private const int Photos = 5;
    private const int Videos = 2;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly PeopleViewModel _people;

    public PersonScreenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-person-screen-{Guid.NewGuid():N}");
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

        _people.SetColumns(4);
    }

    [Fact]
    public async Task TheFilterShowsAllThenOnlyPhotosThenOnlyVideos()
    {
        await OpenAsync("Ana");

        Assert.True(_people.ShowingAllKinds);
        Assert.Equal(Photos + Videos, _people.ShownCount);

        await _people.ShowKindCommand.ExecuteAsync("photos");
        Assert.True(_people.ShowingPhotosOnly);
        Assert.Equal(Photos, _people.ShownCount);
        Assert.All(_people.Photos.Tiles, tile => Assert.Equal(AssetKind.Photo, tile.Item.Kind));

        await _people.ShowKindCommand.ExecuteAsync("videos");
        Assert.True(_people.ShowingVideosOnly);
        Assert.Equal(Videos, _people.ShownCount);
        Assert.All(_people.Photos.Tiles, tile => Assert.Equal(AssetKind.Video, tile.Item.Kind));

        await _people.ShowKindCommand.ExecuteAsync("all");
        Assert.Equal(Photos + Videos, _people.ShownCount);

        // The count under the name is everything they are in, whatever is shown.
        Assert.Equal(Photos + Videos, _people.PhotoCount);
    }

    [Fact]
    public async Task AKindTheyHaveNoneOf_SaysSoRatherThanShowingAnEmptyGrid()
    {
        await OpenAsync("Ben");
        await _people.ShowKindCommand.ExecuteAsync("videos");

        Assert.True(_people.HasNothingOfKind);
        Assert.Equal("No videos of Ben yet.", _people.NothingOfKindNote);
    }

    [Fact]
    public async Task TheFilterIsKeptWhenAnotherPersonIsOpened()
    {
        await OpenAsync("Ana");
        await _people.ShowKindCommand.ExecuteAsync("videos");

        await OpenAsync("Ben");

        Assert.True(_people.ShowingVideosOnly);
        Assert.Equal(0, _people.ShownCount);
    }

    [Fact]
    public async Task TheSubtitleCountsTheirPicturesAndSaysTheYearOnceKnown()
    {
        await OpenAsync("Ana");

        Assert.Equal("7 pictures", _people.PersonSubtitle);
        Assert.True(_people.HasNoBirthYear);

        _people.EditBirthYearCommand.Execute(null);
        Assert.True(_people.IsEditingBirthYear);

        _people.BirthYearText = "2015";
        await _people.SetBirthYearCommand.ExecuteAsync(null);

        Assert.False(_people.IsEditingBirthYear);
        Assert.Equal("7 pictures · born 2015", _people.PersonSubtitle);
        Assert.False(_people.HasNoBirthYear);
    }

    [Fact]
    public async Task AnImpossibleYear_IsRefusedAndStaysOpenToCorrect()
    {
        await OpenAsync("Ana");
        _people.EditBirthYearCommand.Execute(null);

        _people.BirthYearText = "1700";
        await _people.SetBirthYearCommand.ExecuteAsync(null);

        Assert.True(_people.IsEditingBirthYear);
        Assert.NotEmpty(_people.BirthYearError);
        Assert.True(_people.HasNoBirthYear);
    }

    [Fact]
    public async Task Renaming_ChangesTheNameEverywhereOnTheScreen()
    {
        await OpenAsync("Ana");

        _people.StartRenameCommand.Execute(null);
        Assert.Equal("Ana", _people.RenameText);

        _people.RenameText = "Ana Lim";
        await _people.RenameCommand.ExecuteAsync(null);

        Assert.False(_people.IsRenaming);
        Assert.Equal("Ana Lim", _people.SelectedPersonName);
        Assert.Contains(_people.Named, person => person.DisplayName == "Ana Lim");
        Assert.Equal("Ana Lim", ReadName(_people.SelectedPerson!.Id));
    }

    [Fact]
    public async Task ANameSomebodyElseHas_IsRefused()
    {
        await OpenAsync("Ana");

        _people.StartRenameCommand.Execute(null);
        _people.RenameText = "ben";
        await _people.RenameCommand.ExecuteAsync(null);

        Assert.True(_people.IsRenaming);
        Assert.Equal("Somebody is already called ben.", _people.RenameError);
        Assert.Equal("Ana", ReadName(_people.SelectedPerson!.Id));
    }

    [Fact]
    public void TheBarCountsTheFacesWaitingAndNamesThePerson()
    {
        _people.SelectedPerson = new PersonItem(
            new PersonSummary(1, "Ana", ConfirmedFaces: 7, Photos: 7, AwaitingReview: 6, null));

        Assert.Equal("Photo Gallery identified 6 more faces that might be Ana", _people.ReviewNotice);
        Assert.Equal(
            "Say yes or no to each. Only the faces you confirm will be tagged as Ana and shown here.",
            _people.ReviewHint);
        Assert.Equal("Review 6 faces", _people.ReviewCaption);

        _people.SelectedPerson = new PersonItem(
            new PersonSummary(1, "Ana", ConfirmedFaces: 7, Photos: 7, AwaitingReview: 1, null));

        Assert.Equal("Photo Gallery identified 1 more face that might be Ana", _people.ReviewNotice);
        Assert.Equal("Review 1 face", _people.ReviewCaption);
    }

    private async Task OpenAsync(string name)
    {
        await _people.ReloadAsync();
        _people.SelectedPerson = _people.Named.Single(person => person.DisplayName == name);
        await TestWait.ForAsync(
            () => _people.PhotoCount > 0 && _people.SelectedPersonName == name,
            $"{name}'s pictures to load");
    }

    private string ReadName(int personId)
    {
        using IServiceScope scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GalleryDbContext>()
            .Set<Person>().AsNoTracking().Single(person => person.Id == personId).DisplayName;
    }

    /// <summary>
    /// Ana in five photographs and two videos; Ben in one photograph.
    /// </summary>
    private void Seed()
    {
        using IServiceScope scope = _services.CreateScope();
        GalleryDbContext db = scope.ServiceProvider.GetRequiredService<GalleryDbContext>();

        db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });

        var ana = new Person { DisplayName = "Ana" };
        var ben = new Person { DisplayName = "Ben" };
        db.Set<Person>().AddRange(ana, ben);

        int id = 0;
        Asset Add(AssetKind kind)
        {
            id++;
            var asset = new Asset
            {
                Id = id,
                PhotoSourceId = 1,
                RelativePath = kind == AssetKind.Video ? $@"2020\{id}.mp4" : $@"2020\{id}.jpg",
                Length = 1,
                ModifiedUtc = new DateTime(2020, 1, 1).AddMinutes(id),
                CreatedUtc = new DateTime(2020, 1, 1),
                IndexedUtc = new DateTime(2020, 1, 1),
                Kind = kind,
                Status = AssetStatus.Ready,

                // The people screen counts only pictures that have been
                // prepared; no file is written, so nothing ever decodes.
                ThumbnailName = $"{id:D4}.jpg",
            };
            db.Assets.Add(asset);
            return asset;
        }

        List<(Asset Asset, Person Person)> named =
        [
            .. Enumerable.Range(0, Photos).Select(_ => (Add(AssetKind.Photo), ana)),
            .. Enumerable.Range(0, Videos).Select(_ => (Add(AssetKind.Video), ana)),
            (Add(AssetKind.Photo), ben),
        ];

        db.SaveChanges();

        foreach ((Asset asset, Person person) in named)
        {
            var face = new Face
            {
                AssetId = asset.Id,
                Bounds = new FaceBounds(10, 10, 40, 40),
                DetectScore = 0.9f,
                Embedding = TestEmbeddings.At(0),
            };
            db.Faces.Add(face);
            db.SaveChanges();

            db.Set<FaceAssignment>().Add(new FaceAssignment
            {
                FaceId = face.Id,
                PersonId = person.Id,
                Source = AssignmentSource.Confirmed,
                DecidedUtc = new DateTime(2020, 1, 2),
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
