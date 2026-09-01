namespace PhotoGallery.Domain.Albums;

/// <summary>
/// A shelf of albums: Holiday over Genting, Bali and Phuket.
/// </summary>
/// <remarks>
/// One level above an album and one level only. The albums do the finding - a
/// rule, a span of days, the people in a picture - and a collection only says
/// which of them belong together. It has no rule of its own for that reason,
/// and no photographs: everything in it is in it because an album is.
///
/// <para>It also has no origin. An album carries one because the clusterer
/// proposes albums and a pass has to know what it may touch; nobody can propose
/// a theme, so there is no rebuilt form of a collection to protect and no pass
/// ever writes one.</para>
/// </remarks>
public sealed class Collection
{
    public int Id { get; set; }

    /// <summary>
    /// Which collection this is, on every machine that has been told about it.
    /// </summary>
    /// <remarks>
    /// Minted where it is declared, for the same reason an album's and a
    /// person's are. Nothing reads it yet - collections do not travel between
    /// machines in this release - and it is here from the first migration so
    /// that they can later without a migration over rows that already exist in
    /// several libraries. See docs/prp/12-sharing.md for what a decision has to
    /// carry.
    /// </remarks>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>What it is called. Always typed by a person.</summary>
    public required string Name { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// When the name was last typed.
    /// </summary>
    /// <remarks>
    /// The same date-rather-than-flag rule the rest of the model follows, and
    /// what a merge would need to decide whose name is newer. Not null the way
    /// an album's is: an album can carry a name no person chose, and a
    /// collection cannot exist without somebody naming it.
    /// </remarks>
    public DateTime NamedUtc { get; set; }

    /// <summary>
    /// When this collection was removed, or null while it is still on the
    /// screen.
    /// </summary>
    /// <remarks>
    /// A tombstone, kept for ever, for the same reason an album's is: without
    /// one, a merge from a machine that still holds it would put it back.
    /// </remarks>
    public DateTime? DeletedUtc { get; set; }
}
