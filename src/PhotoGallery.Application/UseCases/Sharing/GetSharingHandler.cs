using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// What the Sync screen opens showing.
/// </summary>
/// <remarks>
/// Answered before anything is nominated, because the screen has to say what
/// syncing is for before the user chooses a folder rather than after.
///
/// <para>Nothing here reads a decision: a directory listing, some counts and the
/// machines already heard from. The counts used to be free as well, and are not
/// any more - <see cref="IDecisionReader.HeldStandingAsync"/> walks the index
/// once whenever anything is held, because a count that could not say <em>why</em>
/// was a count the screen then described wrongly. A library with nothing held
/// still costs nothing.</para>
///
/// <para><strong>The folder is asked under the deadline the scan keeps.</strong>
/// It used to be waited on for as long as the operating system took to give up
/// on it: twenty-three seconds on the family's library with the drive in the
/// house switched off, with nothing on the screen to say why and Sync now live
/// throughout. See <see cref="SyncFolderProbe"/>.</para>
/// </remarks>
public sealed class GetSharingHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionExchange _exchange;
    private readonly SyncFolderProbe _folder;

    public GetSharingHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionExchange exchange,
        SyncFolderProbe folder)
    {
        _index = index;
        _decisions = decisions;
        _exchange = exchange;
        _folder = folder;
    }

    /// <summary>
    /// The folder this library syncs through, from the library alone - before
    /// anything asks the folder itself.
    /// </summary>
    /// <remarks>
    /// So that the screen can name the folder it is checking while it checks.
    /// Read only as part of <see cref="HandleAsync"/>, the folder's box said "No
    /// folder chosen yet" for as long as the folder took to answer, about a
    /// folder that was chosen.
    /// </remarks>
    public async Task<string> FolderAsync(CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        return settings.SharedFolder ?? string.Empty;
    }

    public async Task<SharingStatus> HandleAsync(CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        // No folder yet is the ordinary first state, and the answer calls it no
        // problem: the screen is about to ask for one.
        SyncFolderAnswer answer =
            await _folder.AskAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        string folder = settings.SharedFolder ?? string.Empty;

        IReadOnlyList<KnownMachine> known =
            await _decisions.KnownMachinesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PublishedAnswers> published = answer.CanExchange
            ? await _exchange.StandingAsync(cancellationToken).ConfigureAwait(false)
            : [];

        HeldStanding held =
            await _decisions.HeldStandingAsync(cancellationToken).ConfigureAwait(false);

        return new SharingStatus(folder, answer.Problem, Standing(known, published, settings), held);
    }

    /// <summary>
    /// Every other machine, whether this library has heard from it or only seen
    /// its file.
    /// </summary>
    /// <remarks>
    /// The two halves answer different questions and both are needed. A machine
    /// that has published but not yet been merged from is the ordinary state five
    /// seconds before somebody presses the button; one that has been merged from
    /// but whose file is gone is a laptop that has left the house, and its name
    /// is the only place that is written down.
    /// </remarks>
    private static List<MachineStanding> Standing(
        IReadOnlyList<KnownMachine> known,
        IReadOnlyList<PublishedAnswers> published,
        LibrarySettings settings)
    {
        Dictionary<Guid, string> names = [];
        foreach (KnownMachine machine in known)
        {
            names[machine.MachineId] = machine.Name;
        }

        Dictionary<Guid, DateTime> shared = [];
        foreach (PublishedAnswers answers in published)
        {
            // Our own file. We know perfectly well when we last wrote it, and a
            // line telling the user their own laptop is up to date with itself
            // is a line that has to be read before it can be skipped.
            if (answers.MachineId == settings.MachineId)
            {
                continue;
            }

            shared[answers.MachineId] = answers.WrittenUtc;
        }

        List<MachineStanding> standing = [];

        foreach (Guid machine in names.Keys.Union(shared.Keys))
        {
            // Described where its name would go, at the start of its own row in
            // the list, so it begins as a name does: with a capital letter.
            standing.Add(new MachineStanding(
                names.TryGetValue(machine, out string? name)
                    ? name
                    : "A computer this library has not taken changes from yet",
                shared.TryGetValue(machine, out DateTime when) ? when : null,
                names.ContainsKey(machine)));
        }

        // Most recently shared first, and the ones that never have at the bottom
        // - which is where somebody looking for a laptop that has gone quiet
        // will look for it.
        return
        [
            .. standing
                .OrderByDescending(machine => machine.SharedUtc ?? DateTime.MinValue)
                .ThenBy(machine => machine.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }
}
