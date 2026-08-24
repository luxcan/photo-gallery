using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Places;

/// <summary>
/// Answers the gallery's search box: which places match what has been typed so
/// far.
/// </summary>
/// <remarks>
/// The same rules as <see cref="People.FindPeopleHandler"/>, deliberately, so
/// the one box behaves one way. Matching on <em>contains</em> rather than the
/// start of the name matters more here than it does for people: the gazetteer
/// names neighbourhoods, so this library's Tampines photographs are filed under
/// "Tampines Estate" and "Tampines New Town", and somebody typing "tampines"
/// would otherwise be told there is no such place.
/// </remarks>
public sealed class FindPlacesHandler
{
    /// <summary>How many places the box offers at once.</summary>
    public const int MaxMatches = 8;

    private readonly IPlaceReader _places;

    public FindPlacesHandler(IPlaceReader places) => _places = places;

    /// <summary>
    /// Places whose name contains what was typed, best first. An empty search
    /// offers the most photographed, so an empty box answers "where have I been?".
    /// </summary>
    public async Task<IReadOnlyList<PlaceDirectoryEntry>> HandleAsync(
        string? search, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PlaceDirectoryEntry> everywhere =
            await _places.GetDirectoryAsync(cancellationToken).ConfigureAwait(false);

        string text = search?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return [.. everywhere.OrderByDescending(entry => entry.Photos).Take(MaxMatches)];
        }

        return
        [
            .. everywhere
                .Where(entry => entry.Name.Contains(
                    text, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(entry => entry.Name.StartsWith(
                    text, StringComparison.CurrentCultureIgnoreCase))
                .ThenByDescending(entry => entry.Photos)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxMatches),
        ];
    }
}
