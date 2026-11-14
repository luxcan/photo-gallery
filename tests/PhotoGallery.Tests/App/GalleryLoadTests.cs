using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Reading the library onto the wall: the newest read wins, the count is right
/// the first time it is shown, and the empty message waits for an answer.
/// </summary>
public sealed class GalleryLoadTests : IDisposable
{
    private readonly GatedReader _reader = new();
    private readonly ListedStore _store = new();
    private readonly ServiceProvider _services;
    private readonly GalleryViewModel _gallery;

    public GalleryLoadTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<IGalleryReader>(_reader)
            .AddTransient<QueryGalleryHandler>()
            .BuildServiceProvider();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(), _store);
    }

    [Fact]
    public async Task TheNewestLoadFillsTheWall_EvenWhenAnOlderOneAnswersLast()
    {
        // Photos pressed, then Videos before Photos had answered. The slower
        // first read used to land second and leave the wall showing photographs
        // under a lit Videos chip.
        Task photos = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 1);
        Task videos = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 2);

        _reader.Answer(2, 10, 11);
        await videos;
        _reader.Answer(1, 1, 2, 3);
        await photos;

        Assert.Equal([10, 11], OnTheWall());
        Assert.Equal(2, _gallery.TotalCount);
        Assert.False(_gallery.IsLoading);
    }

    [Fact]
    public async Task AnOvertakenLoad_WaitsForTheNewestOneBeforeReturning()
    {
        // What the viewer's delete relies on: it reads the wall again and then
        // opens whatever slid into the deleted picture's place. Returning early
        // would open a tile on a wall that is about to be replaced.
        Task first = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 1);
        Task newest = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 2);

        _reader.Answer(1, 1, 2, 3);
        await Task.Delay(100);

        Assert.False(first.IsCompleted, "an overtaken load returned before the newest one");
        Assert.True(_gallery.IsLoading, "an overtaken load put the busy state down");
        Assert.Empty(OnTheWall());

        _reader.Answer(2, 10, 11);
        await first;

        Assert.Equal([10, 11], OnTheWall());
        await newest;
    }

    [Fact]
    public async Task AFailedNewestLoad_IsReportedToEveryCallerWaitingOnIt()
    {
        // Opening the library awaits its read and says when it fails; a folder
        // clicked meanwhile starts a newer read nobody watches. Swallowed in the
        // first caller, that newer read's failure went nowhere and the library
        // carried on as opened on a wall that was never filled.
        Task first = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 1);
        Task newest = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 2);

        _reader.Answer(1, 1);
        _reader.Fail(2, new InvalidOperationException("the index is locked"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => newest);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.False(_gallery.IsLoading);
    }

    [Fact]
    public async Task AFailedOvertakenLoad_StillEndsOnTheNewestWall()
    {
        Task first = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 1);
        Task newest = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 2);

        _reader.Fail(1, new InvalidOperationException("the index is locked"));
        _reader.Answer(2, 10, 11);

        await first;
        await newest;
        Assert.Equal([10, 11], OnTheWall());
    }

    [Fact]
    public async Task TheCountIsRightTheFirstTimeItIsShown()
    {
        // The walk of the thumbnails folder finishes after the read here, and the
        // count must still never appear with every picture "not prepared yet".
        _store.Listed = ["1.jpg", "3.jpg"];
        _store.Hold();
        var summaries = new ConcurrentQueue<string>();
        _gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GalleryViewModel.CountSummary))
            {
                summaries.Enqueue(_gallery.CountSummary);
            }
        };

        Task load = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 1);
        _reader.Answer(1, 1, 2, 3);
        await Task.Delay(100);

        Assert.Empty(summaries);

        _store.Release();
        await load;

        string first = Assert.Single(summaries);
        Assert.EndsWith("1 not prepared yet", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEmptyMessageIsTakenDownTheMomentTheWallIsFilled()
    {
        var announced = new ConcurrentQueue<(bool IsEmpty, int Rows)>();
        _gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GalleryViewModel.IsEmpty))
            {
                announced.Enqueue((_gallery.IsEmpty, _gallery.Rows.Count));
            }
        };

        Task load = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 1);

        // A wall never filled is empty, but says so only once it has an answer.
        Assert.True(_gallery.IsEmpty);
        Assert.False(_gallery.ShowsEmptyMessage);

        _reader.Answer(1, 1, 2);
        await UntilAsync(() => !announced.IsEmpty);

        Assert.Equal((false, 2), announced.First());
        Assert.False(_gallery.ShowsEmptyMessage);
        await load;
    }

    [Fact]
    public async Task AnEmptyAnswer_ShowsTheEmptyMessageOnceTheReadIsOver()
    {
        var shown = new ConcurrentQueue<bool>();
        _gallery.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GalleryViewModel.ShowsEmptyMessage))
            {
                shown.Enqueue(_gallery.ShowsEmptyMessage);
            }
        };

        Task load = _gallery.LoadAsync();
        await UntilAsync(() => _reader.Calls >= 1);
        _reader.Answer(1);
        await load;

        Assert.True(_gallery.ShowsEmptyMessage);
        Assert.True(shown.Last());
    }

    private int[] OnTheWall() =>
        [.. _gallery.Rows.SelectMany(row => row.Tiles).Select(tile => tile.Item.Id)];

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 300 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The asynchronous operation did not finish in time.");
    }

    public void Dispose()
    {
        _store.Release();
        _services.Dispose();
    }

    private static GalleryItem Item(int id) => new(
        id,
        $@"2026\{id}.jpg",
        $"{id}.jpg",
        "2026",
        $@"C:\pictures\2026\{id}.jpg",
        $"{id}.jpg",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        0,
        AssetKind.Photo);

    /// <summary>A reader that answers each query only when the test says.</summary>
    private sealed class GatedReader : IGalleryReader
    {
        private readonly ConcurrentDictionary<int, TaskCompletionSource<GalleryPage>> _answers = [];
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<GalleryPage> QueryAsync(
            GalleryQuery query, CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref _calls);
            return Pending(call).Task;
        }

        public void Answer(int call, params int[] ids) =>
            Pending(call).SetResult(new GalleryPage([.. ids.Select(Item)], ids.Length));

        public void Fail(int call, Exception error) => Pending(call).SetException(error);

        private TaskCompletionSource<GalleryPage> Pending(int call) =>
            _answers.GetOrAdd(call, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

        public Task<IReadOnlyList<PendingThumbnail>> GetThumbnailCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<PendingVideo>> GetVideoCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FaceScanCandidate>> GetFaceCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ContentScanCandidate>> GetContentCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<LocationCandidate>> GetLocationCandidatesAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FolderNode>> GetFoldersAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// A store that lists what it is told to, and can be made to take its time
    /// over it.
    /// </summary>
    private sealed class ListedStore : IThumbnailStore
    {
        private readonly ManualResetEventSlim _open = new(initialState: true);

        public IReadOnlyCollection<string> Listed { get; set; } = [];

        public void Hold() => _open.Reset();

        public void Release() => _open.Set();

        public IReadOnlyCollection<string> ListStoredNames(Action<string, Exception>? unreadable = null)
        {
            _open.Wait(TimeSpan.FromSeconds(10));
            return Listed;
        }

        public Task<string> SaveAsync(GeneratedThumbnail thumbnail, CancellationToken token = default) =>
            Task.FromResult(string.Empty);

        public string NameFor(string contentHash) => contentHash;

        public string ResolveTilePath(string thumbnailName) =>
            Path.Combine(Path.GetTempPath(), $"pg-absent-{thumbnailName}");

        public string ResolvePreviewPath(string thumbnailName) => ResolveTilePath(thumbnailName);

        public bool Exists(string? thumbnailName) => false;

        public DateTime? PreviewWrittenUtc(string? thumbnailName) => null;

        public bool TryDelete(string? thumbnailName) => true;

        public void RemoveEmptyShards()
        {
        }
    }
}
