using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// What is known about a group of photographs, for naming it.
/// </summary>
/// <remarks>
/// Separate from the repository that stores collections because it answers
/// about photographs rather than about collections, and because the naming rung
/// a group lands on depends on how far it is from home - which only the index
/// can say.
/// </remarks>
public interface ICollectionFactsReader
{
    /// <summary>
    /// The places and people in these photographs, commonest first, with the
    /// kind the clusterer settled on.
    /// </summary>
    Task<CollectionFacts> DescribeAsync(
        PhotoGroup group,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The place most of them resolved to, when enough of them agree on one.
    /// </summary>
    Task<int?> PlaceOfAsync(
        IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The photograph to show for a group.
    /// </summary>
    /// <remarks>
    /// One with people in it, because that is what anybody recognises an
    /// occasion by. Falling back to the middle of the span alone produced
    /// covers of a hotel blanket and a ceiling - each of them genuinely the
    /// middle photograph, and none of them any use for finding the holiday
    /// again.
    /// </remarks>
    Task<int> CoverOfAsync(
        IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default);
}
