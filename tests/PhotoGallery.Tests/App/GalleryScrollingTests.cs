using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Dragging the scrollbar the length of a large library used to leave the grid
/// grey and unresponsive: every position the thumb passed through started its
/// own decode of 240 pictures, nothing cancelled the ones already running, and
/// the position finally landed on waited behind all of them.
/// </summary>
/// <remarks>
/// Backed by a real index of 600 pictures, because both rules under test are
/// about telling one scroll position from another and every position in an empty
/// grid is the same one.
/// </remarks>
public sealed class GalleryScrollingTests : IDisposable
{
    private const int Pictures = 600;
    private const int Columns = 20;

    private readonly string _root;
    private readonly ServiceProvider _services;
    private readonly GalleryViewModel _gallery;

    public GalleryScrollingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-scroll-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        string database = Path.Combine(_root, "index.db");
        Seed(database);

        _services = new ServiceCollection()
            .AddDbContext<GalleryDbContext>(
                options => options.UseSqlite($"Data Source={database}"))
            .AddScoped<IGalleryReader, SqliteGalleryReader>()
            .AddTransient<QueryGalleryHandler>()
            .BuildServiceProvider();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    private static void Seed(string database)
    {
        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={database}")
                .Options;

        using var db = new GalleryDbContext(options);
        db.Database.Migrate();
        db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = @"C:\pictures" });

        for (int i = 0; i < Pictures; i++)
        {
            db.Assets.Add(new Asset
            {
                Id = i + 1,
                PhotoSourceId = 1,
                RelativePath = $@"2020\{i:D4}.jpg",
                Length = 1,
                ModifiedUtc = new DateTime(2020, 1, 1).AddMinutes(i),
                CreatedUtc = new DateTime(2020, 1, 1),
                IndexedUtc = new DateTime(2020, 1, 1),
                Kind = AssetKind.Photo,
            });
        }

        db.SaveChanges();
    }

    /// <summary>A loaded grid, laid out as a wide window would lay it out.</summary>
    private async Task ReadyAsync()
    {
        _gallery.SetVisibleRows(9);
        await _gallery.LoadAsync();
        _gallery.SetColumns(Columns);

        Assert.Equal(Pictures, _gallery.TotalCount);
    }

    [Fact]
    public async Task ShowRange_PositionsPassedThroughAreAbandonedForTheLatest()
    {
        await ReadyAsync();

        // A drag is a rapid run of positions. Each one has to cancel the one
        // before it, or they queue: twenty-five in turn, each waiting out its
        // own settle, would take far longer than the last one alone.
        var clock = Stopwatch.StartNew();

        Task[] drag = [.. Enumerable.Range(0, 25).Select(i => _gallery.ShowRangeAsync(i * 20))];
        await Task.WhenAll(drag);

        clock.Stop();
        Assert.True(
            clock.ElapsedMilliseconds < 4_000,
            $"positions were not being cancelled: the drag took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ShowRange_WaitsForTheViewportToSettleBeforeReadingAnything()
    {
        await ReadyAsync();

        // The point of the pause. Landing somewhere and staying there should
        // cost a moment; flying past should cost nothing.
        var clock = Stopwatch.StartNew();
        await _gallery.ShowRangeAsync(0);
        clock.Stop();

        Assert.True(
            clock.ElapsedMilliseconds >= 100,
            $"nothing waited for the viewport to settle: returned in {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ShowRange_IsNotRestartedByTheListReconsideringItsOwnHeight()
    {
        // The bug this exists for. A virtualised list refines its guess at the
        // extent as rows realise, which nudges the reported position by a few
        // pictures and raises a scroll event, at a standstill. Each nudge used to
        // cancel the wait and start it again, so it never finished and the grid
        // stayed grey however long the user waited.
        await ReadyAsync();

        Task settling = _gallery.ShowRangeAsync(300);
        await Task.Delay(200);

        await _gallery.ShowRangeAsync(304);
        await _gallery.ShowRangeAsync(297);

        Assert.False(
            settling.IsCompleted,
            "a nudge smaller than a row cancelled the request that was already in flight");

        await settling;
    }

    [Fact]
    public async Task ShowRange_IsRestartedByAMoveOfAWholeRowOrMore()
    {
        // The other half: a real move must still abandon what was being fetched
        // for where the user no longer is.
        await ReadyAsync();

        Task abandoned = _gallery.ShowRangeAsync(300);
        await Task.Delay(200);

        await _gallery.ShowRangeAsync(300 + Columns);

        Assert.True(
            abandoned.IsCompleted,
            "moving a whole row left the previous position still being fetched");
    }

    [Fact]
    public async Task ShowRange_OfAnEmptyGridDoesNotThrow()
    {
        await _gallery.ShowRangeAsync(0);
        await _gallery.ShowRangeAsync(5_000);
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
            // A temp folder that will not go is not a failed test.
        }
    }
}
