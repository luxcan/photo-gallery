using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// One collection the pass wants to offer: the group, with a name and a cover
/// worked out for it.
/// </summary>
/// <param name="ProposalKey">
/// The run of days it covers. How the store finds the row it wrote last time,
/// and how a rejection outlives that row.
/// </param>
public sealed record ProposedCollection(
    string ProposalKey,
    string Name,
    DateTime StartUtc,
    DateTime EndUtc,
    CollectionKind Kind,
    int? PlaceId,
    int CoverAssetId,
    IReadOnlyList<int> AssetIds);
