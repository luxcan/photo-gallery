using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Places;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Working out where photographs were taken, for the eleven thousand that were
/// indexed before the app knew to look.
/// </summary>
public sealed class LocatePhotosHandlerTests : IDisposable
{
    private const string Away = @"\\nas\away";

    private readonly string _tempRoot;
    private readonly string _photosRoot;
    private readonly GalleryDbContext _db;
    private readonly SqliteAssetRepository _assets;
    private readonly SqliteGalleryReader _reader;
    private readonly SqlitePlaceRepository _places;
    private readonly StubCoordinates _coordinates = new();
    private readonly StubGeocoder _geocoder = new();
    private readonly FakeAvailability _availability = new();

    public LocatePhotosHandlerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-locate-{Guid.NewGuid():N}");
        string workingFolderRoot = Path.Combine(_tempRoot, "library");
        _photosRoot = Path.Combine(_tempRoot, "photos");
        Directory.CreateDirectory(workingFolderRoot);
        Directory.CreateDirectory(_photosRoot);

        DbContextOptions<GalleryDbContext> options =
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(workingFolderRoot, "index.db")}")
                .Options;
        _db = new GalleryDbContext(options);
        _db.Database.Migrate();

        _assets = new SqliteAssetRepository(_db);
        _reader = new SqliteGalleryReader(_db);
        _places = new SqlitePlaceRepository(_db);

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _photosRoot });
        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 2, Path = Away });
        _db.SaveChanges();
    }

    private LocatePhotosHandler NewHandler() =>
        new(_reader, _coordinates, _geocoder, _places, _assets, _availability);

    [Fact]
    public async Task Locate_NamesAPhotographFromItsCoordinates()
    {
        AddPhoto("a.jpg");
        _coordinates.At("a.jpg", 3.1390, 101.6869);
        _geocoder.Nearest = new GazetteerPlace(1735161, "Kuala Lumpur", "MY", "14", 3.14, 101.69, 0.4);

        LocatePhotosResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Examined);
        Assert.Equal(1, result.Located);
        Assert.Equal(1, result.Named);

        Asset photo = await PhotoAsync("a.jpg");
        Assert.Equal(3.1390, photo.Latitude);
        Assert.NotNull(photo.PlaceId);
        Assert.Equal("Kuala Lumpur", (await PlacesAsync()).Single().Name);
    }

    /// <summary>
    /// The whole reason the marker exists.
    /// </summary>
    /// <remarks>
    /// Five photographs in six carry no GPS. If having none left the row looking
    /// exactly like one never examined, every one of them would be opened over
    /// the share again on every run for the rest of the library's life - which is
    /// the mistake this pass was written to correct in the first place.
    /// </remarks>
    [Fact]
    public async Task Locate_RemembersThatAPhotographWithNoGpsWasAsked()
    {
        AddPhoto("nogps.jpg");
        _coordinates.WithNothing("nogps.jpg");

        await NewHandler().HandleAsync();

        Asset photo = await PhotoAsync("nogps.jpg");
        Assert.Null(photo.Latitude);
        Assert.NotNull(photo.LocationReadUtc);

        _coordinates.Reads = 0;
        LocatePhotosResult second = await NewHandler().HandleAsync();

        Assert.Equal(0, second.Examined);
        Assert.Equal(0, _coordinates.Reads);
    }

    /// <summary>
    /// A file that would not open is not an answer, so nothing is written down.
    /// </summary>
    [Fact]
    public async Task Locate_LeavesAnUnreadableFileToBeTriedAgain()
    {
        AddPhoto("broken.jpg");
        _coordinates.Unreadable.Add("broken.jpg");

        LocatePhotosResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Unreadable);
        Assert.Null((await PhotoAsync("broken.jpg")).LocationReadUtc);

        // Offered again, unlike the one that genuinely had no coordinates.
        _coordinates.At("broken.jpg", 3.1390, 101.6869);
        Assert.Equal(1, (await NewHandler().HandleAsync()).Examined);
    }

    /// <summary>
    /// Coordinates already on the row cost no file read at all.
    /// </summary>
    [Fact]
    public async Task Locate_UsesCoordinatesThePreparingPassAlreadyFound()
    {
        AddPhoto("known.jpg", latitude: 3.1390, longitude: 101.6869);
        _geocoder.Nearest = new GazetteerPlace(1735161, "Kuala Lumpur", "MY", "14", 3.14, 101.69, 0.4);

        LocatePhotosResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Named);
        Assert.Equal(0, _coordinates.Reads);
    }

    /// <summary>
    /// An absent share stops the work that needs it, and only that work.
    /// </summary>
    /// <remarks>
    /// Refusing the whole pass would be wrong twice over: the other sources are
    /// still there, and a photograph whose coordinates are already indexed needs
    /// no file at all, so it can be named on a laptop with no NAS in sight.
    /// </remarks>
    [Fact]
    public async Task Locate_SkipsAnUnreachableSourceWithoutTouchingItsPhotographs()
    {
        AddPhoto("here.jpg");
        _coordinates.At("here.jpg", 3.1390, 101.6869);
        AddPhoto("gone.jpg", sourceId: 2);
        AddPhoto("gone-but-known.jpg", sourceId: 2, latitude: 51.5074, longitude: -0.1278);
        _availability.Away.Add(Away);
        _geocoder.Nearest = new GazetteerPlace(1735161, "Somewhere", "MY", "14", 3.14, 101.69, 0.4);

        LocatePhotosResult result = await NewHandler().HandleAsync();

        Assert.Equal([Away], result.UnreachableSources);

        // The one on the absent share was not read and nothing was recorded.
        Assert.Equal(0, _coordinates.ReadsOf("gone.jpg"));
        Assert.Null((await PhotoAsync("gone.jpg")).LocationReadUtc);

        // The reachable one, and the one that needed no file, both finished.
        Assert.Equal(2, result.Examined);
        Assert.NotNull((await PhotoAsync("here.jpg")).LocationReadUtc);
        Assert.NotNull((await PhotoAsync("gone-but-known.jpg")).LocationReadUtc);
    }

    /// <summary>
    /// Too far from anywhere is a settled answer, not an unfinished one.
    /// </summary>
    [Fact]
    public async Task Locate_RemembersAPhotographTooFarFromAnywhereToName()
    {
        AddPhoto("atsea.jpg");
        _coordinates.At("atsea.jpg", 0d, -30d);
        _geocoder.Nearest = null;

        LocatePhotosResult result = await NewHandler().HandleAsync();

        Assert.Equal(1, result.Located);
        Assert.Equal(0, result.Named);

        Asset photo = await PhotoAsync("atsea.jpg");
        Assert.Equal(0d, photo.Latitude);
        Assert.Null(photo.PlaceId);
        Assert.NotNull(photo.LocationReadUtc);

        Assert.Equal(0, (await NewHandler().HandleAsync()).Examined);
    }

    /// <summary>
    /// A holiday's worth of photographs is one place, not two hundred.
    /// </summary>
    [Fact]
    public async Task Locate_KeepsOneRowPerPlaceHoweverManyPhotographsShareIt()
    {
        for (int i = 0; i < 5; i++)
        {
            AddPhoto($"{i}.jpg");
            _coordinates.At($"{i}.jpg", 3.1390, 101.6869);
        }

        _geocoder.Nearest = new GazetteerPlace(1735161, "Kuala Lumpur", "MY", "14", 3.14, 101.69, 0.4);

        await NewHandler().HandleAsync();

        Place place = Assert.Single(await PlacesAsync());
        Assert.Equal(5, await _db.Assets.CountAsync(a => a.PlaceId == place.Id));
    }

    /// <summary>Re-running after a place already exists must not insert a second one.</summary>
    [Fact]
    public async Task Locate_ReusesAPlaceRecordedByAnEarlierRun()
    {
        AddPhoto("a.jpg");
        _coordinates.At("a.jpg", 3.1390, 101.6869);
        _geocoder.Nearest = new GazetteerPlace(1735161, "Kuala Lumpur", "MY", "14", 3.14, 101.69, 0.4);
        await NewHandler().HandleAsync();

        AddPhoto("b.jpg");
        _coordinates.At("b.jpg", 3.1390, 101.6869);
        await NewHandler().HandleAsync();

        Assert.Single(await PlacesAsync());
    }

    [Fact]
    public async Task Locate_KeepsWhatItFinishedWhenStopped()
    {
        for (int i = 0; i < 6; i++)
        {
            AddPhoto($"{i}.jpg");
            _coordinates.At($"{i}.jpg", 3.1390, 101.6869);
        }

        using var cancellation = new CancellationTokenSource();
        _coordinates.OnRead = () => cancellation.Cancel();
        _geocoder.Nearest = new GazetteerPlace(1735161, "Kuala Lumpur", "MY", "14", 3.14, 101.69, 0.4);

        LocatePhotosResult result = await NewHandler()
            .HandleAsync(degreeOfParallelism: 1, progress: null, cancellation.Token);

        Assert.True(result.Cancelled);
        Assert.True(result.Examined < 6, "the pass was not actually interrupted");
    }

    [Fact]
    public async Task Locate_IgnoresVideosAndCopiesSetAside()
    {
        AddPhoto("a.jpg");
        _coordinates.At("a.jpg", 3.1390, 101.6869);
        AddAsset("clip.mov", AssetKind.Video);
        AddPhoto("aside.jpg", quarantined: true);

        Assert.Equal(1, (await NewHandler().HandleAsync()).Examined);
    }

    private void AddPhoto(
        string relativePath,
        int sourceId = 1,
        double? latitude = null,
        double? longitude = null,
        bool quarantined = false) =>
        AddAsset(relativePath, AssetKind.Photo, sourceId, latitude, longitude, quarantined);

    private void AddAsset(
        string relativePath,
        AssetKind kind,
        int sourceId = 1,
        double? latitude = null,
        double? longitude = null,
        bool quarantined = false)
    {
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = sourceId,
            RelativePath = relativePath,
            Kind = kind,
            Status = AssetStatus.Ready,
            Length = 1024,
            ModifiedUtc = DateTime.UtcNow,
            Latitude = latitude,
            Longitude = longitude,
            QuarantinedUtc = quarantined ? DateTime.UtcNow : null,
        });
        _db.SaveChanges();
    }

    private async Task<Asset> PhotoAsync(string relativePath) =>
        await _db.Assets.AsNoTracking().SingleAsync(a => a.RelativePath == relativePath);

    private async Task<List<Place>> PlacesAsync() =>
        await _db.Places.AsNoTracking().ToListAsync();

    /// <summary>Files that carry coordinates, files that carry none, and files that will not open.</summary>
    private sealed class StubCoordinates : IOriginalCoordinates
    {
        private readonly Dictionary<string, CoordinateReading> _answers =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Lock _counting = new();
        private readonly Dictionary<string, int> _reads = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Reads { get; set; }

        public Action? OnRead { get; set; }

        public void At(string name, double latitude, double longitude) =>
            _answers[name] = CoordinateReading.At(latitude, longitude);

        public void WithNothing(string name) => _answers[name] = CoordinateReading.None;

        public int ReadsOf(string name)
        {
            lock (_counting)
            {
                return _reads.TryGetValue(name, out int count) ? count : 0;
            }
        }

        public CoordinateReading Read(string fullPath)
        {
            string name = Path.GetFileName(fullPath);

            lock (_counting)
            {
                Reads++;
                _reads[name] = ReadsOf(name) + 1;
            }

            OnRead?.Invoke();

            return Unreadable.Contains(name)
                ? CoordinateReading.Unreadable
                : _answers.TryGetValue(name, out CoordinateReading answer)
                    ? answer
                    : CoordinateReading.None;
        }
    }

    /// <summary>One answer for every coordinate, which is all these tests need.</summary>
    private sealed class StubGeocoder : IGeocoder
    {
        public GazetteerPlace? Nearest { get; set; }

        public GazetteerPlace? Resolve(double latitude, double longitude) => Nearest;
    }

    /// <summary>Sources that are up, and sources that are away.</summary>
    private sealed class FakeAvailability : ISourceAvailability
    {
        public HashSet<string> Away { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool CanReach(string sourceRoot) => !Away.Contains(sourceRoot);
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
            // A temp folder that will not go is not a failed test.
        }
    }
}
