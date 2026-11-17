using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Takes what the other machines have decided and settles it into this library.
/// </summary>
/// <remarks>
/// The merge itself is a pure function of decision sets and lives in
/// <see cref="DecisionMerge"/>. What is here is the part that cannot be pure:
/// reading, fetching, turning pictures, writing rows, and saying what changed.
///
/// <para><strong>It looks twice when a turn lands.</strong> Turning a photograph
/// rewrites every face's bounds, so a name confirmed on a picture that was later
/// straightened is keyed on a box this machine has not got yet - and the first
/// pass rightly holds it. Once the turn is applied both machines have moved
/// their boxes through the same arithmetic and are in the same frame, so the
/// second pass matches it exactly. An ordering rule rather than a wider key, and
/// this is where the ordering happens.</para>
///
/// <para>The turning itself is <see cref="MergedTurns"/>, shared with the sweep
/// that applies answers which have been waiting for their photographs.</para>
/// </remarks>
public sealed class MergeDecisionsHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionRepository _repository;
    private readonly IDecisionExchange _exchange;
    private readonly MergedTurns _turns;

    public MergeDecisionsHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionRepository repository,
        IDecisionExchange exchange,
        MergedTurns turns)
    {
        _index = index;
        _decisions = decisions;
        _repository = repository;
        _exchange = exchange;
        _turns = turns;
    }

    public async Task<MergeResult> HandleAsync(
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ExchangeReadiness readiness =
            await _exchange.ReadinessAsync(cancellationToken).ConfigureAwait(false);

        if (!readiness.CanExchange)
        {
            return MergeResult.CouldNot(readiness.Problem);
        }

        MachineIdentity machine = await PublishDecisionsHandler
            .ThisMachineAsync(_index, cancellationToken)
            .ConfigureAwait(false);

        FetchedDecisions everything =
            await _exchange.FetchAsync(cancellationToken).ConfigureAwait(false);

        TrustedMachines trust =
            await TrustedMachines.ReadAsync(_decisions, cancellationToken).ConfigureAwait(false);

        (FetchedDecisions fetched, IReadOnlyList<string> unconfirmed) = await TrustedOnlyAsync(
            machine, everything, trust, cancellationToken).ConfigureAwait(false);

        if (fetched.Sets.Count == 0)
        {
            return new MergeResult(
                true, string.Empty, MergeOutcome.Nothing, unconfirmed.Count, fetched.Unreadable, [])
            {
                Unconfirmed = unconfirmed,
            };
        }

        (MergeOutcome outcome, IReadOnlyList<PairingProposal> pairings) = await OnePassAsync(
            machine, fetched, progress, cancellationToken).ConfigureAwait(false);

        // A turn moved the boxes the next answers are keyed on, so the names
        // that were held for want of them can land now. Bounded at one extra
        // pass: nothing a second merge applies moves a box again.
        if (outcome.PhotographsTurned > 0 && !outcome.WasCancelled)
        {
            (MergeOutcome again, _) = await OnePassAsync(
                machine, fetched, progress, cancellationToken).ConfigureAwait(false);

            outcome = Both(outcome, again);
        }

        // Only the machines actually listened to. A refused set is one whose
        // answers were read and then not taken, and remembering it puts that
        // laptop on the Sharing screen as a computer this library has taken
        // answers from - which is the screen's way of saying the two are in
        // step. Two libraries that have never had a folder in common would
        // otherwise report each other as settled from the first press, and the
        // one line that could have said so says the opposite.
        HashSet<Guid> refused = [.. outcome.Refused.Select(set => set.Machine.Id)];

        foreach (DecisionSet them in fetched.Sets.Where(set => !refused.Contains(set.Machine.Id)))
        {
            await _repository
                .RememberAsync(them.Machine, DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }

        return new MergeResult(
            true,
            string.Empty,
            CalledHere(outcome, trust),
            fetched.Sets.Count + unconfirmed.Count,
            fetched.Unreadable,
            CalledHere(pairings, trust))
        {
            Unconfirmed = unconfirmed,
        };
    }

    /// <summary>
    /// The refusals, with each computer named as the screen names it.
    /// </summary>
    /// <remarks>
    /// Only for saying: the merge and the machines it remembered have already
    /// been given the names the files carry, which is what a computer calls
    /// itself and must stay so.
    /// </remarks>
    private static MergeOutcome CalledHere(MergeOutcome outcome, TrustedMachines trust) =>
        outcome with
        {
            Refused =
            [
                .. outcome.Refused.Select(refused => refused with
                {
                    Machine = refused.Machine with { Name = trust.CalledHere(refused.Machine) },
                }),
            ],
        };

    /// <summary>
    /// The folders worth asking about, each with the name somebody here gave
    /// the computer that keeps it.
    /// </summary>
    private static IReadOnlyList<PairingProposal> CalledHere(
        IReadOnlyList<PairingProposal> pairings, TrustedMachines trust) =>
    [
        .. pairings.Select(pairing =>
            pairing with { FamiliarName = trust.FamiliarName(pairing.MachineId) }),
    ];

    /// <summary>
    /// The sets of the computers somebody here has said to take changes from,
    /// and the names of the ones nobody has answered for yet.
    /// </summary>
    /// <remarks>
    /// Held back before the merge sees anything, because every check the merge
    /// makes is on what a file says about itself - and a file can say anything.
    /// A computer met for the first time is written down, so the Sync screen
    /// can ask about it by name; one somebody said "not this one" to is left
    /// out without a word, as it was asked to be.
    /// </remarks>
    private async Task<(FetchedDecisions Trusted, IReadOnlyList<string> Unconfirmed)>
        TrustedOnlyAsync(
            MachineIdentity machine,
            FetchedDecisions fetched,
            TrustedMachines trust,
            CancellationToken cancellationToken)
    {
        List<DecisionSet> trusted = [];
        List<string> unconfirmed = [];

        foreach (DecisionSet them in fetched.Sets)
        {
            if (trust.Takes(them.Machine.Id))
            {
                trusted.Add(them);
                continue;
            }

            // Our own id, in a file that is not ours. The merge would skip it;
            // asking about it would be asking whether to trust this computer.
            if (them.Machine.Id == machine.Id || trust.Ignores(them.Machine.Id))
            {
                continue;
            }

            await _repository.MeetAsync(them.Machine, cancellationToken).ConfigureAwait(false);
            unconfirmed.Add(trust.CalledHere(them.Machine));
        }

        return (fetched with { Sets = trusted }, unconfirmed);
    }

    private async Task<(MergeOutcome Outcome, IReadOnlyList<PairingProposal> Pairings)>
        OnePassAsync(
        MachineIdentity machine,
        FetchedDecisions fetched,
        IProgress<MergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        DecisionSet mine = await _decisions
            .ReadAsync(machine, cancellationToken)
            .ConfigureAwait(false);

        LibraryContents here =
            await _decisions.ContentsAsync(cancellationToken).ConfigureAwait(false);

        MergePlan plan = DecisionMerge.Merge(mine, fetched.Sets, here, DateTime.UtcNow);

        plan = await _turns.CarryOutAsync(plan, cancellationToken).ConfigureAwait(false);

        MergeOutcome outcome = await _repository
            .ApplyAsync(plan, progress, cancellationToken)
            .ConfigureAwait(false);

        return (outcome, plan.Pairings);
    }

    /// <summary>Two passes of one merge, reported as the one thing the user did.</summary>
    private static MergeOutcome Both(MergeOutcome first, MergeOutcome second) =>
        new(
            first.PeopleGained + second.PeopleGained,
            first.PeopleRenamed + second.PeopleRenamed,
            first.PeopleDeleted + second.PeopleDeleted,
            first.NamesGained + second.NamesGained,
            first.NamesReplaced + second.NamesReplaced,
            first.FacesSetAside + second.FacesSetAside,
            first.PhotographsTurned,
            first.AlbumsChanged + second.AlbumsChanged,
            first.PhotographsMoved + second.PhotographsMoved,

            // The second pass is what is still waiting, not the two added
            // together: everything the first pass held that the turns freed has
            // just been applied, and counting it twice would report answers as
            // waiting that are already in.
            second.Held,
            [.. first.Moves, .. second.Moves],
            second.Joins,
            second.Refused,
            second.WasCancelled,
            first.CollectionsChanged + second.CollectionsChanged);
}
