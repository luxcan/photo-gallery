using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.OpenLibrary;
using PhotoGallery.Application.UseCases.Preferences;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Application;

public sealed class SaveThemeHandlerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteLibraryIndex _index;

    public SaveThemeHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-theme-{Guid.NewGuid():N}");
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
    public async Task NewLibrary_FollowsWindowsByDefault()
    {
        LibrarySettings settings = await _index.GetSettingsAsync();

        Assert.Equal(ThemePreference.System, settings.Theme);
    }

    [Fact]
    public async Task SavedTheme_SurvivesAReopen()
    {
        await new SaveThemeHandler(_index).HandleAsync(ThemePreference.Dark);

        // A fresh context proves the value reached the file, not just the
        // change tracker.
        using var reopened = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_tempRoot, "index.db")}")
                .Options);
        var index = new SqliteLibraryIndex(reopened);

        Assert.Equal(ThemePreference.Dark, (await index.GetSettingsAsync()).Theme);
    }

    [Fact]
    public async Task OpenLibrary_ReportsTheSavedTheme()
    {
        await new SaveThemeHandler(_index).HandleAsync(ThemePreference.Light);

        var workingFolder = new WorkingFolder(_tempRoot);
        var handler = new OpenLibraryHandler(
            workingFolder,
            _index,
            new SqliteAssetRepository(_db),
            new JsonAppConfigStore(Path.Combine(_tempRoot, "config.json")));

        OpenLibraryResult result = await handler.HandleAsync();

        Assert.Equal(ThemePreference.Light, result.Theme);
    }

    [Fact]
    public async Task SavingTheSameThemeTwiceIsHarmless()
    {
        var handler = new SaveThemeHandler(_index);

        await handler.HandleAsync(ThemePreference.Dark);
        await handler.HandleAsync(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, (await _index.GetSettingsAsync()).Theme);
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
