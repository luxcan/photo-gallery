using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.People;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>Late reads must not repaint state chosen after they began.</summary>
public sealed class GalleryAsyncResultTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"pg-gallery-races-{Guid.NewGuid():N}");

    public GalleryAsyncResultTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ASlowFaceReadCannotReplaceTheFacesOfTheNewPhoto()
    {
        var people = new DelayedFaces();
        using ServiceProvider services = Services(people);
        GalleryViewModel gallery = Gallery(services);
        gallery.ShowFaceNames = true;

        gallery.OpenTile = Tile(1);
        await UntilAsync(() => people.Calls >= 1);
        gallery.OpenTile = Tile(2);
        await UntilAsync(() => people.Calls >= 2);

        people.Release(2, Face(202));
        await UntilAsync(() => gallery.OpenFaces.SingleOrDefault()?.FaceId == 202);

        people.Release(1, Face(101));
        await UntilAsync(() => people.DirectoryCalls >= 2);
        await Task.Delay(50);

        Assert.Equal(202, Assert.Single(gallery.OpenFaces).FaceId);
    }

    [Fact]
    public async Task ASlowSearchCannotReplaceSuggestionsForTheLatestText()
    {
        var people = new DelayedSearchDirectory();
        using ServiceProvider services = Services(people);
        GalleryViewModel gallery = Gallery(services);

        gallery.SearchText = "Ana";
        await UntilAsync(() => people.Calls >= 1);
        gallery.SearchText = "Bob";
        await UntilAsync(() => people.Calls >= 2);

        people.Release(2);
        await UntilAsync(() => gallery.SearchMatches.SingleOrDefault()?.DisplayName == "Bob Tan");

        people.Release(1);
        await Task.Delay(50);

        Assert.Equal("Bob Tan", Assert.Single(gallery.SearchMatches).DisplayName);
    }

    private GalleryViewModel Gallery(ServiceProvider services)
    {
        var working = new WorkingFolder(_root);
        working.EnsureCreated();
        return new GalleryViewModel(
            services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(working));
    }

    private static ServiceProvider Services(IPeopleReader people) =>
        new ServiceCollection()
            .AddSingleton(people)
            .AddSingleton<IPeopleReader>(people)
            .AddSingleton<IPlaceReader, NoPlaces>()
            .AddTransient<FindPeopleHandler>()
            .AddTransient<FindPlacesHandler>()
            .BuildServiceProvider();

    private static GalleryTile Tile(int id) => new(new GalleryItem(
        id,
        $@"2026\{id}.jpg",
        $"{id}.jpg",
        "2026",
        $@"C:\pictures\2026\{id}.jpg",
        $"{id}.jpg",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        0,
        AssetKind.Photo));

    private static FaceOnPhoto Face(int id) => new(
        id, new FaceBounds(10, 10, 40, 40), 0.99f, null, null, null, IsIgnored: false);

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The asynchronous operation did not finish in time.");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class DelayedFaces : PeopleReaderStub
    {
        private readonly Dictionary<int, TaskCompletionSource<IReadOnlyList<FaceOnPhoto>>> _reads = [];

        public int Calls => _reads.Count;

        public int DirectoryCalls { get; private set; }

        public override Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
            int assetId, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<FaceOnPhoto>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _reads.Add(assetId, completion);
            return completion.Task;
        }

        public override Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default)
        {
            DirectoryCalls++;
            return Task.FromResult<IReadOnlyList<PersonDirectoryEntry>>([]);
        }

        public void Release(int assetId, FaceOnPhoto face) =>
            _reads[assetId].SetResult([face]);
    }

    private sealed class DelayedSearchDirectory : PeopleReaderStub
    {
        private readonly List<TaskCompletionSource<IReadOnlyList<PersonDirectoryEntry>>> _reads = [];

        public int Calls => _reads.Count;

        public override Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<PersonDirectoryEntry>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _reads.Add(completion);
            return completion.Task;
        }

        public void Release(int call) => _reads[call - 1].SetResult(
        [
            new PersonDirectoryEntry(1, "Ana Lim", 3),
            new PersonDirectoryEntry(2, "Bob Tan", 4),
        ]);
    }

    private abstract class PeopleReaderStub : IPeopleReader
    {
        public virtual Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
            int assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public virtual Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FaceRecord>> GetFacesAsync(
            bool includeEmbeddings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FaceSample>> GetSamplesAsync(
            int personId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Person>> GetPeopleAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FaceRejection>> GetRejectionsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoPlaces : IPlaceReader
    {
        public Task<IReadOnlyList<PlaceDirectoryEntry>> GetDirectoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceDirectoryEntry>>([]);
    }
}
