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
    /// <summary>
    /// Photographs turned here, whose boxes are in the turned frame.
    /// </summary>
    /// <remarks>
    /// Set beside the constructor because only taking another machine's faces
    /// asks it. A face travels with no rotation, and the machine that drew it may
    /// never have turned the photograph or heard of the turn - so its box is an
    /// upright one, and on a photograph turned here it would sit in the wrong
    /// place and be turned again by the next merge of the turn.
    /// </remarks>
    public IReadOnlySet<AssetKey> Turned { get; init; } = new HashSet<AssetKey>();

    /// <summary>
    /// The picture each photograph has here, by rendition name.
    /// </summary>
    /// <remarks>
    /// Set beside the constructor for the same reason as <see cref="Turned"/>:
    /// only taking another machine's faces asks it. A box means something only
    /// on the picture it was drawn on, and a rendition is named after the bytes
    /// it was made from - so two machines with the same name for a photograph's
    /// picture are looking at the same picture, and two with different names are
    /// not, whatever the path says.
    /// </remarks>
    public IReadOnlyDictionary<AssetKey, string> Renditions { get; init; } =
        new Dictionary<AssetKey, string>();

    /// <summary>
    /// Photographs whose faces here describe a picture that has since changed,
    /// and are waiting for the face pass to replace them.
    /// </summary>
    /// <remarks>
    /// The crawl clears the marker that says a photograph was looked at when it
    /// finds the file changed, and leaves the faces themselves for the face pass,
    /// which deletes them and draws new ones later in the same scan. Until then
    /// they are not faces on this picture, and judging another machine's faces
    /// against them - or marking the photograph looked at by taking any - would
    /// keep them for good.
    /// </remarks>
    public IReadOnlySet<AssetKey> Outdated { get; init; } = new HashSet<AssetKey>();

    /// <summary>A library that has indexed nothing at all.</summary>
    public static LibraryContents Empty { get; } = new(
        new HashSet<Guid>(),
        new HashSet<AssetKey>(),
        new Dictionary<AssetKey, IReadOnlyList<FaceBounds>>());
}
