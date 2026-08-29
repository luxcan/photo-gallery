using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.Ports;

/// <summary>The write side: carrying out what a merge decided.</summary>
public interface IDecisionRepository
{
    /// <summary>
    /// Applies a plan, and answers what it actually changed.
    /// </summary>
    /// <remarks>
    /// Reported by count and by kind rather than as a success, because a merge
    /// that says nothing is a merge nobody can trust or undo.
    ///
    /// <para>Stoppable at any point. What has been applied is applied and what
    /// has not is picked up next time, which costs nothing to arrange: the merge
    /// reads the whole state every run, so a plan half carried out is simply a
    /// library the next one has less to do to.</para>
    /// </remarks>
    Task<MergeOutcome> ApplyAsync(
        MergePlan plan,
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the turns that have already been carried out on the pictures.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ApplyAsync"/>, and called before it, because a
    /// turn is the one merged decision that is not only a row: the cached
    /// pictures move and the boxes drawn on them move with them. Nothing is
    /// recorded until they have, so a rendition that could not be read leaves
    /// the library exactly as it was - the same order a turn made by hand keeps.
    ///
    /// <para>The moment and the machine are the ones from the answer, not this
    /// machine and not now. A turn that lost its author would be republished as
    /// this library's own decision.</para>
    /// </remarks>
    Task RecordTurnsAsync(
        IReadOnlyList<Domain.Sharing.PhotoTurn> turns,
        IReadOnlyDictionary<Domain.Sharing.AssetKey, int> rows,
        CancellationToken cancellationToken = default);

    /// <summary>Records that a machine was heard from, and when.</summary>
    Task RememberAsync(
        MachineIdentity machine,
        DateTime mergedUtc,
        CancellationToken cancellationToken = default);
}
