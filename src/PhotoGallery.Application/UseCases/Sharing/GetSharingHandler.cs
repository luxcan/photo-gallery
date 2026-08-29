using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// What the Sharing screen opens showing.
/// </summary>
/// <remarks>
/// Answered before anything is nominated, because the screen has to say what
/// will and will not be sent before the user chooses a folder rather than after.
///
/// <para>Nothing here reads a decision. A directory listing, a count and the
/// machines already heard from - so opening the screen on a library of sixteen
/// thousand photographs costs the same as opening it on an empty one.</para>
/// </remarks>
public sealed class GetSharingHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionExchange _exchange;

    public GetSharingHandler(
        ILibraryIndex index, IDecisionReader decisions, IDecisionExchange exchange)
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

        IReadOnlyList<Peer> known =
            await _decisions.PeersAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PublishedAnswers> published = folder.Length > 0 && readiness.CanExchange
            ? await _exchange.StandingAsync(cancellationToken).ConfigureAwait(false)
            : [];

        int waiting = await _decisions.WaitingCountAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<Unprepared> outstanding =
            await _decisions.UnpreparedAsync(cancellationToken).ConfigureAwait(false);

        return new SharingStatus(
            folder,
            problem,
            Standing(known, published, settings),
            waiting,
            outstanding.Count);
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
        IReadOnlyList<Peer> known,
        IReadOnlyList<PublishedAnswers> published,
        LibrarySettings settings)
    {
        Dictionary<Guid, string> names = [];
        foreach (Peer peer in known)
        {
            names[peer.MachineId] = peer.Name;
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
            standing.Add(new MachineStanding(
                names.TryGetValue(machine, out string? name)
                    ? name
                    : "a computer this library has not taken answers from yet",
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
