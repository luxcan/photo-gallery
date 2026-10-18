using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Asks the sync folder whether it is there, and stops waiting for the answer
/// at a deadline.
/// </summary>
/// <remarks>
/// <strong>One question, one deadline, for everybody who asks.</strong> A scan
/// asks before it takes or gives the small copies, the Sync screen asks as it
/// opens, and Sync now asks before it takes anybody's answers. The screen used
/// to ask with no deadline at all: on the family's library, with the drive in
/// the house switched off, it said nothing for twenty-three seconds while a scan
/// asking the same folder gave up after fifteen - and Sync now could be pressed
/// for all of them. Written once, here, so the three cannot drift apart again.
///
/// <para><strong>Raced, not awaited.</strong> Asking is <c>Directory.Exists</c>
/// underneath, which cannot be cancelled and holds whoever asked until the
/// operating system gives up on an address nothing is answering. So it is asked
/// through the pool's question, which is written not to hold its caller, and
/// abandoned if the deadline comes first. The question left behind finishes on
/// its own long before anybody asks again.</para>
/// </remarks>
public sealed class SyncFolderProbe
{
    /// <summary>What a scan's overlay says while the folder is asked whether it is there.</summary>
    private const string Looking = "Looking in the sync folder";

    private readonly ILibraryIndex _index;
    private readonly IRenditionPool _pool;

    public SyncFolderProbe(ILibraryIndex index, IRenditionPool pool)
    {
        _index = index;
        _pool = pool;
    }

    /// <summary>
    /// How long the folder has to answer before whoever asked goes on without
    /// it.
    /// </summary>
    /// <remarks>
    /// Twenty-one seconds is how long the operating system took to give up on an
    /// address nothing was answering, measured on this network. Fifteen is long
    /// enough for a sleeping drive to wake, which is the ordinary case of a
    /// folder that is there; short enough that a laptop out of the house loses a
    /// quarter of a minute and no more.
    /// </remarks>
    public static TimeSpan Deadline { get; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long this one waits: <see cref="Deadline"/>, unless a test says
    /// otherwise so that it need not wait it out.
    /// </summary>
    public TimeSpan Patience { get; init; } = Deadline;

    /// <summary>
    /// Asks the folder whether it is there, and answers by the deadline whether
    /// it has or not.
    /// </summary>
    /// <param name="progress">
    /// Told the folder is being asked once there is a folder to ask, and not
    /// before. A library nobody shares has nothing to wait for, and a phase with
    /// nothing to do says nothing.
    /// </param>
    /// <param name="cancellationToken">
    /// Ends the wait, not the question: the question cannot be cancelled
    /// underneath.
    /// </param>
    public async Task<SyncFolderAnswer> AskAsync(
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        string? folder = settings.SharedFolder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            return SyncFolderAnswer.NotChosen;
        }

        // Named before the wait rather than after it, so that a folder slow to
        // answer is not waited on under the name of whatever came before.
        progress?.Report(new MergeProgress(Looking, 0, 0));

        // Asked uncancelled: the question cannot be cancelled underneath anyway,
        // and one left behind finishes on its own.
        Task<ExchangeReadiness> asking = _pool.ReadinessAsync(CancellationToken.None);

        Task answered = await Task
            .WhenAny(asking, Task.Delay(Patience, cancellationToken))
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (answered != asking)
        {
            return SyncFolderAnswer.Silent(folder, Patience);
        }

        ExchangeReadiness readiness = await asking.ConfigureAwait(false);

        return readiness.CanExchange
            ? SyncFolderAnswer.There(folder)
            : SyncFolderAnswer.Unreachable(folder, readiness.Problem);
    }
}
