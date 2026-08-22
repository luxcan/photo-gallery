using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Places;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IPlaceReader"/>
public sealed class SqlitePlaceReader : IPlaceReader
{
    private readonly GalleryDbContext _db;

    public SqlitePlaceReader(GalleryDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlaceDirectoryEntry>> GetDirectoryAsync(
        CancellationToken cancellationToken = default)
    {
        // One grouped query, unlike the people directory's two. A photograph
        // holds many faces and so belongs to many people, which is what makes
        // that count awkward; it was taken in exactly one place.
        //
        // Copies set aside as redundant are left out for the reason the grid
        // leaves them out: they are not in the library, and counting them would
        // promise pictures the search cannot then show.
        var counted = await _db.Assets
            .AsNoTracking()
            .Where(a => a.PlaceId != null
                     && a.Kind == AssetKind.Photo
                     && a.QuarantinedUtc == null)
            .GroupBy(a => a.PlaceId!.Value)
            .Select(group => new { PlaceId = group.Key, Photos = group.Count() })
            .Join(
                _db.Places,
                count => count.PlaceId,
                place => place.Id,
                (count, place) => new
                {
                    place.Id, place.Name, place.CountryCode, place.Admin1Code, count.Photos,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<PlaceDirectoryEntry> places =
        [
            .. counted.Select(entry => new PlaceDirectoryEntry(
                PlaceFilter.Exactly(entry.Id), entry.Name, entry.Photos)),
        ];

        // Regions between the two, on the same rule: offered only when they hold
        // more than one place, and only when they have a name to be typed.
        List<PlaceDirectoryEntry> regions =
        [
            .. counted
                .Where(entry => entry.CountryCode is not null && entry.Admin1Code is not null)
                .GroupBy(
                    entry => $"{entry.CountryCode}.{entry.Admin1Code}",
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => new
                {
                    Country = group.First().CountryCode!,
                    Admin1 = group.First().Admin1Code!,
                    Name = RegionNames.Of(group.First().CountryCode, group.First().Admin1Code),
                    Photos = group.Sum(entry => entry.Photos),
                })
                .Where(region => region.Name is not null)
                .Select(region => new PlaceDirectoryEntry(
                    PlaceFilter.InRegion(region.Country, region.Admin1),
                    region.Name!,
                    region.Photos)),
        ];

        // Rolled up here rather than in a second query. A library has as many
        // places as it has been to - hundreds, not millions - and the counts are
        // already in hand.
        //
        // A country with only one place in it is left out: "Hong Kong" beside
        // "Tsim Sha Tsui" earns its place in the list, but "Norway" beside
        // "Longyearbyen" is the same row twice with the same count.
        List<PlaceDirectoryEntry> countries =
        [
            .. counted
                .Where(entry => entry.CountryCode is not null)
                .GroupBy(entry => entry.CountryCode!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => new
                {
                    Code = group.Key,
                    Name = CountryNames.Of(group.Key),
                    Photos = group.Sum(entry => entry.Photos),
                })

                // No name, no entry. A row reading "HK" would be worse than no
                // row: it is not what anyone would type.
                .Where(country => country.Name is not null)
                .Select(country => new PlaceDirectoryEntry(
                    PlaceFilter.InCountry(country.Code), country.Name!, country.Photos)),
        ];

        return
        [
            .. places
                .Concat(regions)
                .Concat(countries)
                .OrderByDescending(entry => entry.Photos)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }
}
