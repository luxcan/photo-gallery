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
/// </remarks>
public sealed class GetSharingHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionExchange _exchange;

    public GetSharingHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionExchange exchange)
    {
        _index = index;
        _decisions = decisions;
        _exchange = exchange;
    }

    public async Task<SharingStatus> HandleAsync(CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        ExchangeReadiness readiness =
            await _exchange.ReadinessAsync(cancellationToken).ConfigureAwait(false);

        string folder = settings.SharedFolder ?? string.Empty;

        // No folder yet is the ordinary first state, not a problem to report: the
        // screen is about to ask for one, and saying "choose a folder" beside the
        // button that chooses it is telling somebody what they can already see.
        string problem = folder.Length > 0 && !readiness.CanExchange
            ? readiness.Problem
            : string.Empty;

        IReadOnlyList<KnownMachine> known =
            await _decisions.KnownMachinesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PublishedAnswers> published = folder.Length > 0 && readiness.CanExchange
            ? await _exchange.StandingAsync(cancellationToken).ConfigureAwait(false)
            : [];

        HeldStanding held =
            await _decisions.HeldStandingAsync(cancellationToken).ConfigureAwait(false);

        return new SharingStatus(folder, problem, Standing(known, published, settings), held);
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
                    : "A computer this library has not taken answers from yet",
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
