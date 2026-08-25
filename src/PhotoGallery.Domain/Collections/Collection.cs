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
