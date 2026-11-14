using System.Diagnostics;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Tests.App;

/// <summary>
/// A wall that has just been filled decodes its first screen at once, and only
/// scrolling brings back the pause before decoding.
/// </summary>
/// <remarks>
/// The pause is a second long and was paid on every chip and every visit to the
/// library, where nothing is moving. It is still wanted for scrolling, so these
/// pin both halves.
/// </remarks>
public sealed class TileWindowFreshFillTests
{
    /// <summary>Comfortably under the one-second pause, so a wait shows.</summary>
    private const int QuickerThanThePauseMs = 800;

    [Fact]
    public void MarkPrepared_MarksOnlyTheTilesTheStoreListed()
    {
        var window = new TileWindow(new SlowStore(TimeSpan.Zero));
        window.Fill(Tiles(4));

        window.MarkPrepared(["2", "4"]);

        Assert.Equal([false, true, false, true], window.Tiles.Select(tile => tile.IsPrepared));
    }

    [Fact]
    public async Task AFreshWall_DecodesItsFirstScreenWithoutThePause()
    {
        var window = new TileWindow(new SlowStore(TimeSpan.Zero));
        window.SetColumns(4);
        window.Fill(Tiles(100));

        Assert.True(window.IsFresh);

        var clock = Stopwatch.StartNew();
        Task showing = window.ShowRangeAsync(0);
        await UntilAsync(() => !window.IsFresh);
        clock.Stop();

        Assert.True(
            clock.ElapsedMilliseconds < QuickerThanThePauseMs,
            $"the first screen of a fresh wall waited {clock.ElapsedMilliseconds} ms");
        await showing;
    }

    [Fact]
    public async Task AFreshWall_StaysFreshWhileTheWindowLearnsItsSize()
    {
        // The library's first visit asks for the top, then asks again when the
        // window learns its height and again for its width. Each re-ask cancels
        // the one before, so a pause on any of them is a pause on the screen.
        // The slow store holds each ask's decode long enough that none of them
        // can finish before the next one replaces it.
        var store = new SlowStore(TimeSpan.FromMilliseconds(20));
        var window = new TileWindow(store);
        window.SetColumns(4);
        window.Fill(Tiles(100));

        var clock = Stopwatch.StartNew();
        _ = window.ShowRangeAsync(0);
        window.SetVisibleRows(9);
        window.SetColumns(5);
        Task last = window.ShowRangeAsync(0);

        Assert.True(window.IsFresh, "re-asking for the same screen ended the fresh state");

        await UntilAsync(() => !window.IsFresh);
        clock.Stop();

        Assert.True(
            clock.ElapsedMilliseconds < QuickerThanThePauseMs,
            $"a re-ask on a fresh wall waited: the first screen took {clock.ElapsedMilliseconds} ms");
        await last;
    }

    [Fact]
    public async Task Scrolling_BringsThePauseBack()
    {
        var store = new SlowStore(TimeSpan.Zero);
        var window = new TileWindow(store);
        window.SetColumns(4);
        window.SetVisibleRows(3);
        window.Fill(Tiles(200));

        Task scrolled = window.ShowRangeAsync(80);

        Assert.False(window.IsFresh);

        // Still waiting for the viewport to settle, so nothing has been read.
        await Task.Delay(300);
        Assert.Equal(0, store.Reads);

        await scrolled;
        Assert.True(store.Reads > 0);
    }

    [Fact]
    public async Task Filling_MakesTheWallFreshAgain()
    {
        var window = new TileWindow(new SlowStore(TimeSpan.Zero));
        window.SetColumns(4);
        window.Fill(Tiles(100));
        await window.ShowRangeAsync(0);
        Assert.False(window.IsFresh);

        window.Fill(Tiles(50));

        Assert.True(window.IsFresh);
    }

    private static IEnumerable<GalleryTile> Tiles(int count) =>
        Enumerable.Range(1, count).Select(id => new GalleryTile(new GalleryItem(
            id,
            $@"2020\{id}.jpg",
            $"{id}.jpg",
            "2020",
            $@"C:\one\2020\{id}.jpg",
            $"{id}",
            new DateTime(2020, 6, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 6, 3, 12, 0, 0, DateTimeKind.Utc),
            0,
            AssetKind.Photo)));

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition(), "The first screen never finished.");
    }

    /// <summary>
    /// A store holding nothing, that takes a set time to say so for each tile -
    /// which is how long a decode takes from the window's side.
    /// </summary>
    private sealed class SlowStore(TimeSpan perTile) : IThumbnailStore
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public Task<string> SaveAsync(GeneratedThumbnail thumbnail, CancellationToken token = default) =>
            Task.FromResult(string.Empty);

        public string NameFor(string contentHash) => contentHash;

        public string ResolveTilePath(string thumbnailName)
        {
            Interlocked.Increment(ref _reads);
            Thread.Sleep(perTile);
            return Path.Combine(Path.GetTempPath(), $"pg-absent-{thumbnailName}.jpg");
        }

        public string ResolvePreviewPath(string thumbnailName) => thumbnailName;

        public bool Exists(string? thumbnailName) => false;

        public DateTime? PreviewWrittenUtc(string? thumbnailName) => null;

        public bool TryDelete(string? thumbnailName) => true;

        public IReadOnlyCollection<string> ListStoredNames(Action<string, Exception>? unreadable = null) => [];

        public void RemoveEmptyShards()
        {
        }
    }
}
