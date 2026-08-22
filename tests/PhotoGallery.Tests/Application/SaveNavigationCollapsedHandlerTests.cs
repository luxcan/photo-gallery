using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Application.UseCases.Preferences;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// How wide the user left the side nav belongs to the library, beside the
/// palette and the zoom, so it opens the way it was left.
/// </summary>
public sealed class SaveNavigationCollapsedHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;

    public SaveNavigationCollapsedHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-nav-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();
        _index = new SqliteLibraryIndex(_db);
    }

    [Fact]
    public async Task ANewLibrary_OpensWithTheNavShowing()
    {
        LibrarySettings settings = await _index.GetSettingsAsync();

        Assert.False(settings.NavigationCollapsed);
    }

    [Fact]
    public async Task AFold_SurvivesAReopen()
    {
        await new SaveNavigationCollapsedHandler(_index).HandleAsync(collapsed: true);

        // A fresh context proves the value reached the file, not just the
        // change tracker.
        using var reopened = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options);
        var index = new SqliteLibraryIndex(reopened);

        Assert.True((await index.GetSettingsAsync()).NavigationCollapsed);
    }

    [Fact]
    public async Task SavingTheSameFoldTwiceIsHarmless()
    {
        var handler = new SaveNavigationCollapsedHandler(_index);

        await handler.HandleAsync(collapsed: true);
        await handler.HandleAsync(collapsed: true);

        Assert.True((await _index.GetSettingsAsync()).NavigationCollapsed);
    }

    [Fact]
    public async Task OpenLibrary_ReportsTheStoredFold()
    {
        await new SaveNavigationCollapsedHandler(_index).HandleAsync(collapsed: true);

        var handler = new OpenLibraryHandler(
            new WorkingFolder(_tempRoot),
            _index,
            new SqliteAssetRepository(_db),
            new JsonAppConfigStore(Path.Combine(_tempRoot, "config.json")));

        OpenLibraryResult result = await handler.HandleAsync();

        Assert.True(result.NavigationCollapsed);
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
        }
    }
}
