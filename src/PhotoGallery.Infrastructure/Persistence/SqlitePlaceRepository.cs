using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Places;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="IPlaceRepository"/>
public sealed class SqlitePlaceRepository : IPlaceRepository
{
    private readonly GalleryDbContext _db;

    public SqlitePlaceRepository(GalleryDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<int, int>> EnsureAsync(
        IReadOnlyList<GazetteerPlace> places, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(places);

        if (places.Count == 0)
        {
            return new Dictionary<int, int>();
        }

        // Distinct first: a batch of twenty photographs from one afternoon
        // resolves to one town twenty times over.
        List<GazetteerPlace> wanted =
            [.. places.GroupBy(place => place.GeoNameId).Select(group => group.First())];

        int[] identifiers = [.. wanted.Select(place => place.GeoNameId)];

        Dictionary<int, int> byGeoNameId = await _db.Places
            .AsNoTracking()
            .Where(place => identifiers.Contains(place.GeoNameId))
            .ToDictionaryAsync(place => place.GeoNameId, place => place.Id, cancellationToken)
            .ConfigureAwait(false);

        List<Place> missing =
        [
            .. wanted
                .Where(place => !byGeoNameId.ContainsKey(place.GeoNameId))
                .Select(place => new Place
                {
                    GeoNameId = place.GeoNameId,
                    Name = place.Name,
                    CountryCode = place.CountryCode,
                    Admin1Code = place.Admin1Code,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                }),
        ];

        if (missing.Count > 0)
        {
            _db.Places.AddRange(missing);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            foreach (Place place in missing)
            {
                byGeoNameId[place.GeoNameId] = place.Id;
            }
        }

        return byGeoNameId;
    }
}
