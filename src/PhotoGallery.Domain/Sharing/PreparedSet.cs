namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// What one machine has already worked out about its photographs, offered whole.
/// </summary>
/// <remarks>
/// The manifest the pool needs and the decisions do not. A video's rendition
/// names can be worked out by the machine that wants them - the digest is seeded
/// from the path, the length, the modified time and the frame ordinal, all of
/// which its own crawl knows for free. A photograph's rendition is named after a
/// hash of its bytes, and the bytes are exactly what the receiving machine is
/// trying to avoid reading. It cannot name the file it wants, and that single
/// fact is why this exists.
///
/// <para>Published beside the decisions rather than inside them, because the two
/// have different shapes and different costs: decisions are one small document
/// written whole, renditions are tens of thousands of files copied one at a time
/// and stopped halfway more often than not. Keeping them apart is also what lets
/// a machine take the answers and decline the gigabytes.</para>
/// </remarks>
/// <param name="Models">
/// The model files this machine ran, by name and digest. Carried so that vectors
/// are only ever accepted from a machine running the same ones: an embedding is
/// meaningless outside the model that produced it, and a mismatched one does not
/// fail - it returns a confident answer about the wrong person.
/// </param>
public sealed record PreparedSet(
    MachineIdentity Machine,
    DateTime WrittenUtc,
    IReadOnlyList<PreparedFact> Facts,
    IReadOnlyDictionary<string, string> Models)
{
    public static PreparedSet Empty(MachineIdentity machine, DateTime writtenUtc) =>
        new(machine, writtenUtc, [], new Dictionary<string, string>());

    /// <summary>
    /// The rendition names this set offers, each of which is a pair of files.
    /// </summary>
    public IReadOnlyCollection<string> Pictures =>
        new HashSet<string>(
            Facts.Where(fact => fact.HasPicture).Select(fact => fact.ThumbnailName!),
            StringComparer.OrdinalIgnoreCase);
}
