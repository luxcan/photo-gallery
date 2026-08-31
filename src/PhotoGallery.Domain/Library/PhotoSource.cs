namespace PhotoGallery.Domain.Library;

/// <summary>
/// One place photos come from: a folder on this PC, an external drive, or a
/// network share. A library can aggregate any number of them.
/// </summary>
/// <remarks>
/// Scanning normally only reads a source. A confirmed album action may move an
/// original within the same source and update that asset's relative path; every
/// asset still remembers which source it came from, so a source can be detached
/// without disturbing the others.
/// </remarks>
public sealed class PhotoSource
{
    public int Id { get; set; }

    /// <summary>Root of the source, e.g. <c>D:\Camera Dumps</c> or a UNC path.</summary>
    public required string Path { get; set; }

    /// <summary>
    /// The identity this source shares with the same folder on another machine.
    /// </summary>
    /// <remarks>
    /// Minted locally when the source is added, and replaced by the other
    /// machine's when the two are paired, so that both then agree. It exists
    /// because matching roots is not string equality and must not be built as
    /// though it were: a UNC path on one laptop and a mapped drive letter on
    /// another are the same folder reached two ways, and Windows offers to map
    /// that letter for you - so comparing the text would lock out a family member
    /// for doing a normal thing, with nothing in the app to undo it.
    ///
    /// <para>A manifest names its roots, obvious pairs are proposed, the user
    /// confirms once, and the pairing is remembered here. From then on this is
    /// what a decision is scoped by, never the root itself: that is machine-local
    /// text, and putting it in a key would rebuild the drive-letter problem one
    /// layer down.</para>
    ///
    /// <para>Minted where it is declared, for the reason
    /// <see cref="People.Person.PublicId"/> is.</para>
    /// </remarks>
    public Guid SharedId { get; set; } = Guid.NewGuid();

    public DateTime AddedUtc { get; set; }

    public DateTime? LastScanUtc { get; set; }
}
