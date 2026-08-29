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

        FetchedDecisions fetched =
            await _exchange.FetchAsync(cancellationToken).ConfigureAwait(false);

        if (fetched.Sets.Count == 0)
        {
            return new MergeResult(
                true, string.Empty, MergeOutcome.Nothing, 0, fetched.Unreadable, []);
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

        foreach (DecisionSet them in fetched.Sets)
        {
            await _repository
                .RememberAsync(them.Machine, DateTime.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }

        return new MergeResult(
            true, string.Empty, outcome, fetched.Sets.Count, fetched.Unreadable, pairings);
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
            second.WasCancelled);
}
