using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// Everything about one face that naming people needs to know.
/// </summary>
/// <remarks>
/// The whole library's worth of these is loaded at once and worked on in memory.
/// Around 18,000 faces at two kilobytes each is under 40 MB, and every question
/// this feature asks - which faces look alike, which person does this resemble,
/// who is still unnamed - is a comparison against all of them. A store that
/// could answer those in place would be a great deal of machinery to avoid one
/// second of loading.
/// </remarks>
/// <param name="TakenUtc">
/// The best date available: the photograph's own, or its folder's, or failing
/// both the file's. Resolved once here so that grouping and eras never have to
/// wonder.
/// </param>
public sealed record FaceRecord(
    int FaceId,
    int AssetId,
    string ThumbnailName,
    FaceBounds Bounds,
    float DetectScore,
    DateTime TakenUtc,
    string RelativePath,
    string FullPath,
    FaceEmbedding Embedding,
    int? PersonId,
    AssignmentSource? Source,
    bool IsIgnored)
{
    /// <summary>Whether anyone has said who this is.</summary>
    public bool IsNamed => Source == AssignmentSource.Confirmed;

    /// <summary>
    /// A face still worth asking about: nobody has named it, and nobody has set
    /// it aside as a stranger.
    /// </summary>
    public bool IsUnclaimed => Source is null && !IsIgnored;
}
