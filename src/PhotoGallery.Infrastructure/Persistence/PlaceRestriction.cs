using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Persistence;

/// <summary>
/// Narrowing a set of photographs to somewhere, at either scope.
/// </summary>
/// <remarks>
/// One definition because two things need it and they need it to agree: the grid
/// filters assets directly, and the description search filters content rows by
/// the assets they belong to. A place that meant one thing to the grid and
/// another to the ranking would answer the same question two ways.
/// </remarks>
internal static class PlaceRestriction
{
    public static IQueryable<Asset> Apply(
        IQueryable<Asset> assets, PlaceFilter place, GalleryDbContext db)
    {
        switch (place.Scope)
        {
            case PlaceScope.Place:
                // A column on the row: a photograph was taken in exactly one
                // place, where it can hold any number of people.
                int placeId = place.PlaceId;
                return assets.Where(a => a.PlaceId == placeId);

            case PlaceScope.Region:
                string? inCountry = place.CountryCode;
                string? admin1 = place.Admin1Code;
                return assets.Where(a => db.Places.Any(p =>
                    p.Id == a.PlaceId && p.CountryCode == inCountry && p.Admin1Code == admin1));

            case PlaceScope.Country:
                string? country = place.CountryCode;
                return assets.Where(a =>
                    db.Places.Any(p => p.Id == a.PlaceId && p.CountryCode == country));

            default:
                // Deliberately loud. Returning everything unfiltered would show
                // the whole library under a heading naming one country, which is
                // the kind of wrong that looks like it worked.
                throw new ArgumentOutOfRangeException(
                    nameof(place), place.Scope, "No such place scope.");
        }
    }
}
