using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// What this machine's own scan has actually indexed, which is what decides
/// whether an answer lands or waits.
/// </summary>
/// <remarks>
/// The merge cannot get this from a decision set. A library holds a great many
/// photographs nobody has decided anything about, so "do I have this picture?"
/// and "have I said anything about it?" are different questions, and answering
/// the first with the second would hold answers about photographs sitting right
/// there.
/// </remarks>
/// <param name="Sources">
/// The shared ids this library holds sources for. A machine with none of these
/// in common has nothing to say rather than nothing to add, and the two need
/// different things said about them.
/// </param>
/// <param name="Photographs">Every photograph and video the crawl has indexed.</param>
/// <param name="Faces">
/// The boxes the face pass has found, by photograph. A photograph indexed but
/// not yet looked at for faces is in <paramref name="Photographs"/> and absent
/// here, and answers about its faces wait - which is right, and is why the sweep
/// that applies them runs after the face phase rather than after indexing.
/// </param>
public sealed record LibraryContents(
    IReadOnlySet<Guid> Sources,
    IReadOnlySet<AssetKey> Photographs,
    IReadOnlyDictionary<AssetKey, IReadOnlyList<FaceBounds>> Faces)
{
    /// <summary>A library that has indexed nothing at all.</summary>
    public static LibraryContents Empty { get; } = new(
        new HashSet<Guid>(),
        new HashSet<AssetKey>(),
        new Dictionary<AssetKey, IReadOnlyList<FaceBounds>>());
}
