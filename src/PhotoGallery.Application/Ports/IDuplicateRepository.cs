using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Application.Ports;

/// <summary>Reads and writes what the duplicate passes have found.</summary>
public interface IDuplicateRepository
{
    /// <summary>
    /// Every photograph a duplicate pass needs to weigh: the ones still in the
    /// library that have been prepared.
    /// </summary>
    /// <remarks>
    /// Quarantined copies are left out. They are already set aside, and offering
    /// one as a duplicate of the copy it was set aside for would be a loop.
    /// </remarks>
    Task<IReadOnlyList<Asset>> GetCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every unresolved set of one kind with what the pass just found.
    /// </summary>
    /// <remarks>
    /// Resolved sets survive, because they record a decision the user made. A
    /// pass is only ever allowed to revise its own outstanding questions.
    /// </remarks>
    Task<int> ReplaceAsync(
        DuplicateKind kind,
        IReadOnlyList<DuplicateSet> sets,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DuplicateSetView>> GetAsync(
        DuplicateKind kind, CancellationToken cancellationToken = default);

    Task<DuplicateSetView?> FindAsync(int setId, CancellationToken cancellationToken = default);

    /// <summary>Marks a set as dealt with, so it stops being offered.</summary>
    Task MarkResolvedAsync(
        int setId, bool resolved, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes one copy the one that stays, and every other copy in its set
    /// redundant.
    /// </summary>
    /// <remarks>
    /// The app's choice is a rule applied to folder names and file sizes; the
    /// user's is a decision about a photograph. Where they disagree the user
    /// wins, and this is how they say so.
    /// </remarks>
    Task SetKeeperAsync(
        int setId, int assetId, CancellationToken cancellationToken = default);

    /// <summary>Sets aside or brings back copies, by their asset ids.</summary>
    Task SetQuarantinedAsync(
        IReadOnlyList<int> assetIds,
        DateTime? quarantinedUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Everything currently set aside, newest first.</summary>
    Task<IReadOnlyList<QuarantinedCopy>> GetQuarantinedAsync(
        CancellationToken cancellationToken = default);
}
