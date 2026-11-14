using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;
using PhotoGallery.App.ViewModels;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Duplicates;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Imaging;
using PhotoGallery.Infrastructure.Persistence;
using PhotoGallery.Infrastructure.Storage;
using PhotoGallery.Tests.Sharing;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The number beside Duplicates in the side menu follows a search.
/// </summary>
/// <remarks>
/// It used to stay at what the library held when it was opened: a search that
/// found new groups left the menu saying 497 beside a tab saying 503.
/// </remarks>
public sealed class DuplicatesNavCountTests : IDisposable
{
    private static readonly DateTime s_when = new(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly ServiceProvider _services;
    private readonly MainViewModel _main;

    public DuplicatesNavCountTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-navcount-{Guid.NewGuid():N}");
        string photos = Path.Combine(_root, "photos");
        Directory.CreateDirectory(photos);

        var workingFolder = new WorkingFolder(Path.Combine(_root, "library"));
        workingFolder.EnsureCreated();
        var thumbnails = new FileSystemThumbnailStore(workingFolder);

        _db = new GalleryDbContext(new DbContextOptionsBuilder<GalleryDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
            .Options);
        _db.Database.Migrate();
        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = photos });
        _db.SaveChanges();

        // A real container with the real handlers, the way the app resolves them.
        var quality = new SqlitePhotoQualityRepository(_db);
        _services = new ServiceCollection()
            .AddSingleton<IDuplicateRepository>(new SqliteDuplicateRepository(_db))
            .AddSingleton<IPhotoQualityRepository>(quality)
            .AddSingleton<IModelStore>(new StubModelStore())
            .AddSingleton(new CopyMeasurer(quality, new PreviewQualityReader(), thumbnails, new NoEyes()))
            .AddSingleton<ILibraryIndex>(new SqliteLibraryIndex(_db))
            .AddScoped<FindDuplicatesHandler>()
            .AddScoped<GetDuplicatesHandler>()
            .BuildServiceProvider();

        IServiceScopeFactory scopeFactory = _services.GetRequiredService<IServiceScopeFactory>();
        _main = new MainViewModel(
            scopeFactory,
            new GalleryViewModel(scopeFactory, thumbnails),
            new PeopleViewModel(scopeFactory, thumbnails),
            new DuplicatesViewModel(scopeFactory, thumbnails),
            thumbnails,
            new SilentLog());
    }

    [Fact]
    public async Task LookingForDuplicates_UpdatesTheSideMenuCount()
    {
        AddPhoto(@"2023\a.jpg", hash: "aaaa");
        AddPhoto(@"2023 copy\a.jpg", hash: "aaaa");
        Assert.Equal(0, _main.Counts.UnresolvedDuplicateSets);

        await _main.FindDuplicatesCommand.ExecuteAsync(null);

        Assert.Equal(1, _main.Counts.UnresolvedDuplicateSets);
    }

    private void AddPhoto(string relativePath, string hash)
    {
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Length = 1000,
            ModifiedUtc = s_when,
            CreatedUtc = s_when,
            IndexedUtc = s_when,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            ContentHash = hash,
            PerceptualHash = new PerceptualHash(0),
            ThumbnailName = $"{hash}-thumb.jpg",
            Width = 100,
            Height = 100,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    /// <summary>No eye model here: identical copies are never measured.</summary>
    private sealed class NoEyes : IEyeMeasurer
    {
        public Task<IReadOnlyList<EyeMeasurement>?> MeasureAsync(
            string previewPath, IReadOnlyList<FaceBounds> faces, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The eye model is not installed in these tests.");
    }

    private sealed class SilentLog : IActivityLog
    {
        public void Append(string line)
        {
        }
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
