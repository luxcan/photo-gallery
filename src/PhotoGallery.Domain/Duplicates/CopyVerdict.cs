namespace PhotoGallery.Domain.Duplicates;

/// <summary>What the ladder made of one copy.</summary>
/// <param name="AssetId">The copy.</param>
/// <param name="LostOn">
/// The rung at which it dropped out of the running, or null for the suggested
/// copy and for every copy of a set where nothing was suggested and it was
/// never beaten.
/// </param>
/// <param name="Exposure">Which way it is wrong, when it lost on exposure.</param>
/// <param name="ClosedFaces">
/// The known faces counted against it on eyes: closed here and open in another
/// copy still in the running. Empty unless it lost on eyes, so a face that is
/// closed in every copy, closed here and unknown elsewhere, or closed in a copy
/// that went on because the eyes tied, never gets a marker.
/// </param>
public sealed record CopyVerdict(
    int AssetId,
    KeeperCriterion? LostOn,
    ExposureFault Exposure,
    IReadOnlyList<CopyFace> ClosedFaces);
