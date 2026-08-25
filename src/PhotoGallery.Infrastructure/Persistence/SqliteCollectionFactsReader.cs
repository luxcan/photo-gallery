using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Infrastructure.Persistence;

/// <inheritdoc cref="ICollectionFactsReader"/>
public sealed class SqliteCollectionFactsReader : ICollectionFactsReader
{
    /// <summary>
    /// How much of a group has to resolve to one place before it is called that
    /// place.
    /// </summary>
    /// <remarks>
    /// A fifth. Coordinates are on about one photograph in nine, so demanding a
    /// majority would leave almost every occasion unnamed - and a fifth of a
    /// weekend's photographs agreeing on a town is a good deal more evidence
    /// than the alternative rung, which is the month.
    /// </remarks>
    private const double PlaceShare = 0.2d;

    /// <summary>How many people a title may draw on.</summary>
    private const int MostPeople = 3;

    private readonly GalleryDbContext _db;

    public SqliteCollectionFactsReader(GalleryDbContext db) => _db = db;

    public async Task<CollectionFacts> DescribeAsync(
        PhotoGroup group,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(assetIds);

        List<string> places = await _db.Assets
            .AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id) && asset.PlaceId != null)
            .Join(
                _db.Places.AsNoTracking(),
                asset => asset.PlaceId,
                place => place.Id,
                (asset, place) => place.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Only people the user named themselves. A proposal is never one of the
        // names in a title, because a title that turns out to be a guess costs
        // more trust than an unnamed occasion.
        List<string> people = await _db.FaceAssignments
            .AsNoTracking()
            .Where(assignment => assignment.Source == Domain.People.AssignmentSource.Confirmed)
            .Join(
                _db.Faces.AsNoTracking().Where(face => assetIds.Contains(face.AssetId)),
                assignment => assignment.FaceId,
                face => face.Id,
                (assignment, face) => assignment.PersonId)
            .Join(
                _db.People.AsNoTracking(),
                personId => personId,
                person => person.Id,
                (personId, person) => person.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CollectionFacts(
            group.Kind,
            group.StartUtc,
            group.EndUtc,
            Commonest(places, assetIds.Count * PlaceShare),
            Commonest(people, atLeast: 1).Take(MostPeople).ToList(),
            assetIds.Count);
    }

    public async Task<int?> PlaceOfAsync(
        IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        // Counted in the database rather than by materialising the rows: this
        // runs once per proposed collection, and a library has hundreds.
        var byPlace = await _db.Assets
            .AsNoTracking()
            .Where(asset => assetIds.Contains(asset.Id) && asset.PlaceId != null)
            .GroupBy(asset => asset.PlaceId!.Value)
            .Select(place => new { PlaceId = place.Key, Count = place.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var best = byPlace.MaxBy(place => place.Count);

        return best is not null && best.Count >= assetIds.Count * PlaceShare
            ? best.PlaceId
            : null;
    }

    /// <summary>The names that appear most, and often enough to be worth saying.</summary>
    private static List<string> Commonest(List<string> names, double atLeast) =>
    [
        .. names
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(name => name.Count() >= atLeast)
            .OrderByDescending(name => name.Count())
            .ThenBy(name => name.Key, StringComparer.Ordinal)
            .Select(name => name.Key),
    ];
}
