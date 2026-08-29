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
/// <para><strong>A merged turn writes no original.</strong> Turning by hand
/// tells the file which way up it goes where the file will take it; doing that
/// on every machine afterwards would queue four laptops for an exclusive write
/// on one file on the share, and each that won would change its modified time -
/// invalidating that photograph's rendition for everybody, repeatedly. The
/// person who turned it has already told the file. Sharing carries only what the
/// file would not take.</para>
/// </remarks>
public sealed class MergeDecisionsHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionRepository _repository;
    private readonly IDecisionExchange _exchange;
    private readonly IRenditionTurner _renditions;
    private readonly IFaceRepository _faces;

    public MergeDecisionsHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionRepository repository,
        IDecisionExchange exchange,
        IRenditionTurner renditions,
        IFaceRepository faces)
    {
        _index = index;
        _decisions = decisions;
        _repository = repository;
        _exchange = exchange;
        _renditions = renditions;
        _faces = faces;
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
                true, string.Empty, MergeOutcome.Nothing, 0, fetched.Unreadable);
        }

        MergeOutcome outcome = await OnePassAsync(
            machine, fetched, progress, cancellationToken).ConfigureAwait(false);

        // A turn moved the boxes the next answers are keyed on, so the names
        // that were held for want of them can land now. Bounded at one extra
        // pass: nothing a second merge applies moves a box again.
        if (outcome.PhotographsTurned > 0 && !outcome.WasCancelled)
        {
            MergeOutcome again = await OnePassAsync(
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
            true, string.Empty, outcome, fetched.Sets.Count, fetched.Unreadable);
    }

    private async Task<MergeOutcome> OnePassAsync(
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

        plan = await TurnAsync(plan, cancellationToken).ConfigureAwait(false);

        return await _repository
            .ApplyAsync(plan, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns the cached pictures, and answers with a plan holding only the turns
    /// that actually happened.
    /// </summary>
    /// <remarks>
    /// A turn with no rendition yet waits with the held answers rather than being
    /// dropped. Locally that case is right to record nothing - a rendition that
    /// could not be read leaves the library as it was - but a fresh machine
    /// merging every turn before it owns a single rendition would drop all of
    /// them and then publish its own upright answer as a competing one.
    /// </remarks>
    private async Task<MergePlan> TurnAsync(MergePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Turns.Count == 0)
        {
            return plan;
        }

        IReadOnlyList<TurnTarget> targets = await _decisions
            .TurnTargetsAsync([.. plan.Turns.Select(turn => turn.Photo)], cancellationToken)
            .ConfigureAwait(false);

        Dictionary<AssetKey, TurnTarget> byPhoto =
            targets.ToDictionary(target => target.Photo);

        List<PhotoTurn> done = [];
        List<PhotoTurn> waiting = [];
        Dictionary<AssetKey, int> rows = [];

        // Grouped by the rendition rather than by the row. A photograph in this
        // library exists as up to eight files and identical bytes share one
        // cached picture, so turning per row would turn that picture once per
        // copy - two rows of the same photograph would leave it on its side.
        foreach (IGrouping<string, TurnTarget> sharing in targets
            .Where(target => !string.IsNullOrEmpty(target.ThumbnailName))
            .GroupBy(target => target.ThumbnailName!))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<PhotoTurn> theirs =
                [.. plan.Turns.Where(turn => sharing.Any(target => target.Photo == turn.Photo))];

            if (theirs.Count == 0)
            {
                continue;
            }

            TurnTarget first = sharing.First();
            int degrees = Quarter(theirs[0].Rotation - first.Rotation);

            if (degrees != 0)
            {
                if (_renditions.Turn(first.ThumbnailName!, degrees) is not TurnedRendition before)
                {
                    // No picture to turn yet, so nothing is recorded. Held rather
                    // than dropped: a fresh machine merging every turn before it
                    // owns a rendition would lose all of them and then publish
                    // its own upright answer as a competing one.
                    waiting.AddRange(theirs);
                    continue;
                }

                // The boxes move with the picture, through the same arithmetic
                // the other machine used and over the same pre-turn size. That is
                // what puts both libraries in one frame and lets a face be keyed
                // on its box alone.
                await _faces
                    .TurnFacesAsync(
                        [.. sharing.Select(target => target.AssetId)],
                        degrees,
                        before.Width,
                        before.Height,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (PhotoTurn turn in theirs)
            {
                done.Add(turn);
                rows[turn.Photo] = sharing.First(t => t.Photo == turn.Photo).AssetId;
            }
        }

        // Anything whose photograph this library has not prepared at all.
        waiting.AddRange(plan.Turns.Where(turn =>
            !done.Contains(turn) && !waiting.Contains(turn)));

        await _repository.RecordTurnsAsync(done, rows, cancellationToken).ConfigureAwait(false);

        return plan with
        {
            Turns = done,
            Held = plan.Held with { Turns = [.. plan.Held.Turns, .. waiting] },
        };
    }

    /// <summary>A quarter turn clockwise, whichever way round the arithmetic came out.</summary>
    private static int Quarter(int degrees) => (((degrees % 360) + 360) % 360);

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
