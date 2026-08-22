namespace PhotoGallery.Application.Ports;

/// <summary>Writes the places photographs have been resolved to.</summary>
public interface IPlaceRepository
{
    /// <summary>
    /// Makes sure a row exists for each of these places, and says which row each
    /// one is.
    /// </summary>
    /// <remarks>
    /// Keyed on the gazetteer's own identifier rather than the name, because
    /// names are not unique - there are dozens of Springfields - and because the
    /// identifier is stable across gazetteer releases where a name's spelling is
    /// not.
    ///
    /// <para>Called once per batch with the distinct places that batch resolved
    /// to, not once per photograph. A holiday's worth of pictures share one town,
    /// and inserting it two hundred times to keep one row would be two hundred
    /// round trips to learn what the first one said.</para>
    /// </remarks>
    /// <returns>The gazetteer identifier of each place, mapped to its row id.</returns>
    Task<IReadOnlyDictionary<int, int>> EnsureAsync(
        IReadOnlyList<GazetteerPlace> places, CancellationToken cancellationToken = default);
}
