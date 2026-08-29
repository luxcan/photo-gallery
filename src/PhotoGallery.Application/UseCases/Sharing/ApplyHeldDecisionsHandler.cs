using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Applies the answers that have been waiting for their photographs, now that a
/// scan has found some more of them.
/// </summary>
/// <remarks>
/// The half of holding an answer that makes holding it worth anything. Without
/// this the order of operations becomes something the user has to get right -
/// scan first, then share, and if you did it the other way round you silently
/// lost an evening's work. With it, the order does not matter and cannot be got
/// wrong.
///
/// <para><strong>A phase of the scan, not a button.</strong> Nobody would think
/// to press it, because the thing it repairs happened days ago on a different
/// machine. It runs after the faces are found and before the photographs are
/// grouped into occasions: earlier and a name would arrive at a photograph with
/// no face to land on, later and the occasions would be named from people the
/// library was about to learn about.</para>
///
/// <para><strong>Nothing is released until it has landed.</strong> A held row is
/// the only record of an answer somebody spent an evening making, so a sweep
/// that is stopped part-way forgets nothing and simply costs the next one the
/// same work.</para>
/// </remarks>
public sealed class ApplyHeldDecisionsHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionRepository _repository;
    private readonly MergedTurns _turns;

    public ApplyHeldDecisionsHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionRepository repository,
        MergedTurns turns)
    {
        _index = index;
        _decisions = decisions;
        _repository = repository;
        _turns = turns;
    }

    public async Task<HeldResult> HandleAsync(
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        HeldAnswers waiting =
            await _decisions.WaitingAsync(cancellationToken).ConfigureAwait(false);

        // The ordinary case, and the reason this is affordable as a phase: a
        // library nobody shares with has no held rows, so the sweep is one
        // count and nothing else.
        if (waiting.Count == 0)
        {
            return HeldResult.Nothing;
        }

        MachineIdentity machine = await PublishDecisionsHandler
            .ThisMachineAsync(_index, cancellationToken)
            .ConfigureAwait(false);

        DecisionSet mine =
            await _decisions.ReadAsync(machine, cancellationToken).ConfigureAwait(false);

        LibraryContents here =
            await _decisions.ContentsAsync(cancellationToken).ConfigureAwait(false);

        MergePlan plan = DecisionMerge.Rejoin(mine, waiting, here);
        plan = await _turns.CarryOutAsync(plan, cancellationToken).ConfigureAwait(false);

        MergeOutcome outcome = await _repository
            .ApplyAsync(plan, progress, cancellationToken)
            .ConfigureAwait(false);

        // Stopped part-way through applying, so what landed is not knowable from
        // the plan any more. Everything stays held and the next scan does it
        // again, which costs a sweep and loses nothing.
        if (outcome.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            return new HeldResult(0, waiting.Count, true);
        }

        // Everything that was waiting, less everything still waiting. Answers
        // this library already held, and ones a later answer has since beaten,
        // are in neither list - and both are done with, which is what releasing
        // them says.
        HeldAnswers landed = waiting.Except(plan.Held);

        await _repository.ReleaseAsync(landed, cancellationToken).ConfigureAwait(false);

        return new HeldResult(landed.Count, plan.Held.Count, false);
    }
}
