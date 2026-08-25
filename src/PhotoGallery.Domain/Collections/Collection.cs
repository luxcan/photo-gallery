namespace PhotoGallery.Domain.Collections;

/// <summary>
/// A group of photographs that belong together: a weekend away, a birthday, a
/// day out.
/// </summary>
/// <remarks>
/// Proposed by the app or made by the user, and in both cases only ever a view
/// of the library. Nothing on disk is moved, renamed or copied to make one.
/// </remarks>
public sealed class Collection
{
    public int Id { get; set; }

    /// <summary>
    /// What it is called. Written by the namer for a proposal, typed by the
    /// user for anything else.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// When the first and last photographs in it were taken.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTime"/> rather than <see cref="DateTimeOffset"/> because
    /// SQLite cannot order by an offset, and these are read in span order every
    /// time the screen opens. They carry the wall-clock value the camera wrote,
    /// unconverted, exactly as the photographs do.
    /// </remarks>
    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }

    /// <summary>Where it was, when enough of its photographs agree on one.</summary>
    public int? PlaceId { get; set; }

    /// <summary>The photograph shown for it. Zero while it has none.</summary>
    public int CoverAssetId { get; set; }

    public CollectionKind Kind { get; set; }

    public CollectionOrigin Origin { get; set; }

    /// <summary>
    /// The span of days this was grouped from, or null for one the user made.
    /// </summary>
    /// <remarks>
    /// How a rebuild finds the row it wrote last time instead of adding a
    /// second one, and how a rejection outlives the row. See
    /// <see cref="ProposalKey"/> for why it is the days rather than the id.
    /// </remarks>
    public string? ProposalKey { get; set; }

    /// <summary>
    /// The user typed this name, so no pass may write over it.
    /// </summary>
    public bool WasRenamed { get; set; }

    /// <summary>When the pass last wrote this row.</summary>
    public DateTime BuiltUtc { get; set; }

    public List<CollectionMember> Members { get; } = [];
}
