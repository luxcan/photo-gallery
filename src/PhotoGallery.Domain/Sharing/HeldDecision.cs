namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// An answer waiting for its photograph: something another machine decided
/// about a picture this library has not indexed, kept until it has.
/// </summary>
/// <remarks>
/// <strong>The single most important merge rule in the feature.</strong>
/// Without it the order of operations becomes something the user has to get
/// right - scan first, then share, and if you did it the other way round you
/// silently lost an evening's work. With it, the order does not matter and
/// cannot be got wrong.
///
/// <para>The same table answers the opposite case, which is where the app
/// already had a hole. Quarantine does not travel, because the files do: setting
/// a duplicate aside moves it off the shared drive, and every other machine's
/// next scan finds it gone and removes the row. The machine that quarantined it
/// keeps its row deliberately - <em>"its row is the only thing that knows how to
/// put it back, so a scan that took it away would make the quarantine a one-way
/// door"</em> - but that guard protects only that machine. Restore it later and
/// the other laptops re-add a fresh row nobody has ever named. So a removal
/// parks that photograph's decisions here rather than deleting them, and if the
/// file comes back the names come back with it. It covers the ordinary accidents
/// that look identical to a deletion at scan time too: a folder moved and moved
/// back, a drive remounted, a tidy-up somebody undoes.</para>
///
/// <para><strong>Never expired.</strong> A key, a kind and a payload; nine
/// thousand of them is about a megabyte, so there is no reason to tidy them and
/// a real cost to doing it - a held answer that is thrown away is an evening
/// somebody has to spend again.</para>
/// </remarks>
public sealed class HeldDecision
{
    public int Id { get; set; }

    /// <summary>The source the two libraries matched. See <see cref="AssetKey"/>.</summary>
    public Guid SharedSourceId { get; set; }

    /// <summary>Path below that source's root, as <see cref="AssetKey"/> holds it.</summary>
    public required string RelativePath { get; set; }

    public HeldDecisionKind Kind { get; set; }

    /// <summary>
    /// Which part of the photograph this answer is about: a face's box for a
    /// name, an album for a membership, the run of days for a rejection, and
    /// empty for a turn, which is about the whole picture.
    /// </summary>
    /// <remarks>
    /// The key alone is not enough to hold one answer per thing. A photograph
    /// with eight faces in it carries eight names, and a table keyed on the
    /// photograph would keep the last one and lose seven - or, with no key at
    /// all, grow a fresh row on every merge and stop being idempotent. This is
    /// the smallest thing that makes "one row per answer" true.
    /// </remarks>
    public required string Part { get; set; }

    /// <summary>The answer itself, as JSON.</summary>
    public required string Payload { get; set; }

    /// <summary>The machine that decided it, kept so a forwarded answer still says who.</summary>
    public Guid FromMachine { get; set; }

    /// <summary>
    /// When it was decided - not when it arrived. This is what settles it against
    /// a competing answer, so it has to survive being passed on.
    /// </summary>
    public DateTime DecidedUtc { get; set; }

    /// <summary>The photograph this is waiting for.</summary>
    public AssetKey Key => new(SharedSourceId, RelativePath);
}
