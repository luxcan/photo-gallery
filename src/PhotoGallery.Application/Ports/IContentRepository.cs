namespace PhotoGallery.Application.Ports;

/// <summary>What the app has worked out about what its pictures are of.</summary>
public interface IContentRepository
{
    /// <summary>
    /// Records what some previews turned out to be of.
    /// </summary>
    /// <remarks>
    /// One row per photograph rather than per rendition, so that a query can
    /// filter and rank in one place - and rows sharing a rendition each get their
    /// own copy of the same vector, which is three kilobytes to avoid a join on
    /// every search.
    /// </remarks>
    Task SaveAsync(
        IReadOnlyList<ContentScanUpdate> updates, CancellationToken cancellationToken = default);

    /// <summary>
    /// The vectors to rank a typed phrase against.
    /// </summary>
    /// <remarks>
    /// All of them at once, exactly as the faces feature does and for the same
    /// reason: the question is "which of these is nearest", and that is a
    /// comparison against all of them. Around 35 MB for this library, and a
    /// search is one dot product per photograph.
    /// </remarks>
    /// <param name="personId">
    /// Narrows to photographs somebody is confirmed to be in, before anything is
    /// ranked. Doing it the other way round - rank the library, then keep the
    /// ones with the right person - sounds equivalent and is not: the best three
    /// hundred beaches in twelve years of photographs may contain none of that
    /// person at all, and the search would answer "no pictures" while holding
    /// several.
    /// </param>
    /// <param name="place">
    /// Narrows to photographs taken somewhere, before anything is ranked, for
    /// exactly the reason above: the best three hundred beaches in the library
    /// may include none in Hong Kong.
    /// </param>
    Task<IReadOnlyList<ContentVector>> GetVectorsAsync(
        int? personId = null,
        PlaceFilter? place = null,
        CancellationToken cancellationToken = default);

    /// <summary>How many photographs have been described, and how many could be.</summary>
    Task<(int Described, int Total)> CountAsync(CancellationToken cancellationToken = default);
}
