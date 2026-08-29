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
    /// Which album this is, on every machine that has been told about it.
    /// </summary>
    /// <remarks>
    /// Minted where it is declared, for the same reason a person's is: row ids
    /// are local, and a call site that forgot would write a row claiming the same
    /// identity as every other album in the library - failing on the second one
    /// rather than the first.
    ///
    /// <para>A proposal carries one too and does not travel on it. A proposed row
    /// is derived, so it is matched by its <see cref="ProposalKey"/> instead,
    /// which is what survives the rebuild that renumbers it.</para>
    /// </remarks>
    public Guid PublicId { get; set; } = Guid.NewGuid();

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
    /// When the user typed this name, or null while it is still the app's own.
    /// </summary>
    /// <remarks>
    /// A date where a flag used to be. The flag answered the only question one
    /// library has - may a pass write over this name? - and cannot answer the one
    /// two libraries have, which is whose name is newer. Null still means the app
    /// named it, so the rebuild rule is unchanged.
    /// </remarks>
    public DateTime? NamedUtc { get; set; }

    /// <summary>
    /// When this album was deleted, or null while it is still in the library.
    /// </summary>
    /// <remarks>
    /// A tombstone, kept for ever, for the same reason a person's is: without one
    /// the next merge from a machine that still holds the album puts it back.
    /// Only albums somebody made need one - a proposal removed by a rebuild was
    /// never a decision, and a rejection already records the one that was.
    /// </remarks>
    public DateTime? DeletedUtc { get; set; }

    /// <summary>When the pass last wrote this row.</summary>
    public DateTime BuiltUtc { get; set; }

    /// <summary>
    /// The first and last day a photograph may have been taken on to fit this
    /// collection's rule, or null where the rule says nothing about dates.
    /// </summary>
    /// <remarks>
    /// A rule is what makes a collection something you can add to rather than
    /// only a bag you have filled: "these people, in these places, between these
    /// dates" is enough to go and look for what fits. Both ends are optional, so
    /// one day is a rule with the same date at both ends.
    /// </remarks>
    public DateTime? RuleFromUtc { get; set; }

    public DateTime? RuleToUtc { get; set; }

    public List<CollectionMember> Members { get; } = [];

    /// <summary>Everybody a photograph must hold to fit. All of them, not any.</summary>
    public List<CollectionRulePerson> RulePeople { get; } = [];

    /// <summary>The places a photograph may have been taken in. Any of them.</summary>
    public List<CollectionRulePlace> RulePlaces { get; } = [];

    /// <summary>Whether this collection has anything to look for.</summary>
    public bool HasRule =>
        RuleFromUtc is not null
        || RuleToUtc is not null
        || RulePeople.Count > 0
        || RulePlaces.Count > 0;
}
