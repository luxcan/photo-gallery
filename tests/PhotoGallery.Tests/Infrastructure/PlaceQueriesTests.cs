using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Places;
using PhotoGallery.Infrastructure.Persistence;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Reading places back out: the directory the search box offers, the filter the
/// grid applies, and the one row the details panel shows.
/// </summary>
public sealed class PlaceQueriesTests : IDisposable
{
    private readonly string _root;
    private readonly GalleryDbContext _db;
    private readonly SqlitePlaceReader _places;
    private readonly SqliteGalleryReader _gallery;
    private readonly SqliteAssetRepository _assets;

    public PlaceQueriesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-places-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _db = new GalleryDbContext(
            new DbContextOptionsBuilder<GalleryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_root, "index.db")}")
                .Options);
        _db.Database.Migrate();

        _places = new SqlitePlaceReader(_db);
        _gallery = new SqliteGalleryReader(_db);
        _assets = new SqliteAssetRepository(_db);

        _db.Set<PhotoSource>().Add(new PhotoSource { Id = 1, Path = _root });
        _db.Places.Add(new Place
        {
            Id = 1, GeoNameId = 1880252, Name = "Sentosa", CountryCode = "SG",
            Latitude = 1.25, Longitude = 103.83,
        });
        _db.Places.Add(new Place
        {
            Id = 2, GeoNameId = 1735161, Name = "Tampines Estate", CountryCode = "SG",
            Latitude = 1.35, Longitude = 103.94,
        });

        // Hong Kong, whose districts are the whole reason a country scope exists.
        _db.Places.Add(new Place
        {
            Id = 3, GeoNameId = 1819609, Name = "Tsim Sha Tsui", CountryCode = "HK",
            Latitude = 22.2988, Longitude = 114.1722,
        });
        _db.Places.Add(new Place
        {
            Id = 4, GeoNameId = 1819729, Name = "Central", CountryCode = "HK",
            Latitude = 22.2819, Longitude = 114.1582,
        });

        // Two towns in Pahang, which is MY.06 - a real region with a real name,
        // so the region scope is exercised against the shipped table rather than
        // against a code somebody invented.
        _db.Places.Add(new Place
        {
            Id = 6, GeoNameId = 1735100, Name = "Kampung Bukit Tinggi", CountryCode = "MY",
            Admin1Code = "06", Latitude = 3.3494, Longitude = 101.8263,
        });
        _db.Places.Add(new Place
        {
            Id = 7, GeoNameId = 1735101, Name = "Bentong Town", CountryCode = "MY",
            Admin1Code = "06", Latitude = 3.5223, Longitude = 101.9087,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Directory_OffersARegionBetweenThePlaceAndTheCountry()
    {
        AddPhoto("a.jpg", placeId: 6);
        AddPhoto("b.jpg", placeId: 7);

        IReadOnlyList<PlaceDirectoryEntry> directory = await _places.GetDirectoryAsync();

        PlaceDirectoryEntry region = Assert.Single(
            directory, entry => entry.Filter.Scope == PlaceScope.Region);

        Assert.Equal("Pahang", region.Name);
        Assert.Equal(2, region.Photos);
        Assert.Equal(PlaceFilter.InRegion("MY", "06"), region.Filter);
    }

    [Fact]
    public async Task Query_NarrowsTheGridToEveryPlaceInARegion()
    {
        AddPhoto("bentong.jpg", placeId: 7);
        AddPhoto("bukit.jpg", placeId: 6);
        AddPhoto("sentosa.jpg", placeId: 1);

        GalleryPage page = await _gallery.QueryAsync(
            new GalleryQuery(IncludeVideos: false, Place: PlaceFilter.InRegion("MY", "06")));

        Assert.Equal(2, page.Items.Count);
        Assert.DoesNotContain(page.Items, item => item.RelativePath == "sentosa.jpg");
    }

    /// <summary>
    /// Every rung of the address, smallest first.
    /// </summary>
    [Fact]
    public async Task Facts_ReadAsAnAddress()
    {
        AddPhoto("pahang.jpg", placeId: 6);

        PhotoFacts? facts = await _assets.FindFactsAsync(IdOf("pahang.jpg"));

        Assert.Equal("Kampung Bukit Tinggi, Pahang, Malaysia", facts!.PlaceName);
    }

    [Fact]
    public async Task Directory_CountsThePhotographsInEachPlaceMostFirst()
    {
        AddPhoto("a.jpg", placeId: 1);
        AddPhoto("b.jpg", placeId: 2);
        AddPhoto("c.jpg", placeId: 2);
        AddPhoto("nowhere.jpg");

        PlaceDirectoryEntry[] places =
            [.. (await _places.GetDirectoryAsync()).Where(entry => !entry.IsCountry)];

        Assert.Equal(2, places.Length);
        Assert.Equal("Tampines Estate", places[0].Name);
        Assert.Equal(2, places[0].Photos);
        Assert.Equal("Sentosa", places[1].Name);
        Assert.Equal(PlaceScope.Place, places[1].Filter.Scope);
    }

    /// <summary>
    /// A place with no photographs left in the library is not offered.
    /// </summary>
    /// <remarks>
    /// Otherwise the box suggests somewhere, the user picks it, and the grid is
    /// empty - which reads as a broken search rather than as a place they have
    /// since dealt with.
    /// </remarks>
    [Fact]
    public async Task Directory_LeavesOutPlacesWhoseOnlyPhotographsWereSetAside()
    {
        AddPhoto("a.jpg", placeId: 1, quarantined: true);
        AddPhoto("b.jpg", placeId: 2);

        IReadOnlyList<PlaceDirectoryEntry> directory = await _places.GetDirectoryAsync();

        Assert.Equal("Tampines Estate", Assert.Single(directory).Name);
    }

    [Fact]
    public async Task Query_NarrowsTheGridToOnePlace()
    {
        AddPhoto("a.jpg", placeId: 1);
        AddPhoto("b.jpg", placeId: 2);
        AddPhoto("nowhere.jpg");

        GalleryPage page = await _gallery.QueryAsync(
            new GalleryQuery(IncludeVideos: false, Place: PlaceFilter.Exactly(1)));

        Assert.Equal("a.jpg", Path.GetFileName(Assert.Single(page.Items).RelativePath));
    }

    /// <summary>
    /// A person and a place compose rather than one replacing the other.
    /// </summary>
    [Fact]
    public async Task Query_CombinesAPlaceWithAFolderRatherThanReplacingIt()
    {
        AddPhoto(@"trip\a.jpg", placeId: 1);
        AddPhoto(@"trip\b.jpg", placeId: 2);
        AddPhoto(@"home\c.jpg", placeId: 1);

        GalleryPage page = await _gallery.QueryAsync(
            new GalleryQuery(
                PhotoSourceId: 1, FolderPath: "trip", IncludeVideos: false,
                Place: PlaceFilter.Exactly(1)));

        Assert.Equal(@"trip\a.jpg", Assert.Single(page.Items).RelativePath);
    }

    /// <summary>
    /// A country appears in the directory once its districts are more than one.
    /// </summary>
    /// <remarks>
    /// The whole point of the scope. The gazetteer files Hong Kong photographs
    /// under Tsim Sha Tsui and Central, so without this the word somebody would
    /// actually type reaches nothing at all.
    /// </remarks>
    [Fact]
    public async Task Directory_OffersACountryAlongsideItsPlaces()
    {
        AddPhoto("a.jpg", placeId: 3);
        AddPhoto("b.jpg", placeId: 3);
        AddPhoto("c.jpg", placeId: 4);

        IReadOnlyList<PlaceDirectoryEntry> directory = await _places.GetDirectoryAsync();

        PlaceDirectoryEntry country = Assert.Single(directory, entry => entry.IsCountry);
        Assert.Equal("Hong Kong", country.Name);
        Assert.Equal(3, country.Photos);
        Assert.Equal(PlaceFilter.InCountry("HK"), country.Filter);

        // And the districts are still there in their own right.
        Assert.Contains(directory, e => e.Name == "Tsim Sha Tsui" && e.Photos == 2);
    }

    /// <summary>
    /// A country holding one place is the same row twice.
    /// </summary>
    /// <remarks>
    /// "Singapore, 3" above "Sentosa, 3" tells the user nothing and costs them a
    /// choice between two identical answers.
    /// </remarks>
    [Fact]
    public async Task Directory_LeavesOutACountryWithOnlyOnePlaceInIt()
    {
        AddPhoto("a.jpg", placeId: 1);
        AddPhoto("b.jpg", placeId: 1);

        IReadOnlyList<PlaceDirectoryEntry> directory = await _places.GetDirectoryAsync();

        Assert.DoesNotContain(directory, entry => entry.IsCountry);
    }

    [Fact]
    public async Task Query_NarrowsTheGridToEveryPlaceInACountry()
    {
        AddPhoto("kowloon.jpg", placeId: 3);
        AddPhoto("central.jpg", placeId: 4);
        AddPhoto("sentosa.jpg", placeId: 1);
        AddPhoto("nowhere.jpg");

        GalleryPage page = await _gallery.QueryAsync(
            new GalleryQuery(IncludeVideos: false, Place: PlaceFilter.InCountry("HK")));

        Assert.Equal(2, page.Items.Count);
        Assert.DoesNotContain(page.Items, item => item.RelativePath == "sentosa.jpg");
    }

    [Fact]
    public async Task Facts_CarryThePlaceNameWhenThereIsOne()
    {
        AddPhoto("a.jpg", placeId: 3);

        PhotoFacts? facts = await _assets.FindFactsAsync(IdOf("a.jpg"));

        // Smallest first, as an address is written - the district alone means
        // little to anyone who does not already know where it is.
        Assert.Equal("Tsim Sha Tsui, Hong Kong", facts!.PlaceName);
    }

    /// <summary>
    /// A city-state is not named twice.
    /// </summary>
    [Fact]
    public async Task Facts_DoNotRepeatAPlaceThatIsAlsoItsCountry()
    {
        _db.Places.Add(new Place
        {
            Id = 5, GeoNameId = 1880251, Name = "Singapore", CountryCode = "SG",
            Latitude = 1.29, Longitude = 103.85,
        });
        _db.SaveChanges();
        AddPhoto("sg.jpg", placeId: 5);

        PhotoFacts? facts = await _assets.FindFactsAsync(IdOf("sg.jpg"));

        Assert.Equal("Singapore", facts!.PlaceName);
    }

    /// <summary>
    /// No place is null, so the panel can leave the row out entirely.
    /// </summary>
    [Fact]
    public async Task Facts_LeaveThePlaceNullForAPhotographWithNoCoordinates()
    {
        AddPhoto("nowhere.jpg");

        PhotoFacts? facts = await _assets.FindFactsAsync(IdOf("nowhere.jpg"));

        Assert.NotNull(facts);
        Assert.Null(facts!.PlaceName);
    }

    private void AddPhoto(string relativePath, int? placeId = null, bool quarantined = false)
    {
        _db.Assets.Add(new Asset
        {
            PhotoSourceId = 1,
            RelativePath = relativePath,
            Kind = AssetKind.Photo,
            Status = AssetStatus.Ready,
            Length = 1024,
            ModifiedUtc = DateTime.UtcNow,
            ThumbnailName = $"{Guid.NewGuid():N}.jpg",
            PlaceId = placeId,
            QuarantinedUtc = quarantined ? DateTime.UtcNow : null,
        });
        _db.SaveChanges();
    }

    private int IdOf(string relativePath) =>
        _db.Assets.AsNoTracking().Single(a => a.RelativePath == relativePath).Id;

    public void Dispose()
    {
        _db.Dispose();

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
