using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Carries out the turns a merge decided, on the cached pictures and on the
/// boxes drawn over them.
/// </summary>
/// <remarks>
/// Its own collaborator because two things settle turns and both have to move
/// the pictures the same way: a merge from another machine, and the sweep that
/// applies answers which have been waiting for their photographs. A turn is the
/// one merged decision that is not only a row, so the half that is not a row
/// belongs somewhere both can reach.
///
/// <para><strong>A merged turn writes no original.</strong> Turning by hand
/// tells the file which way up it goes where the file will take it; doing that
/// on every machine afterwards would queue four laptops for an exclusive write
/// on one file on the share, and each that won would change its modified time -
/// invalidating that photograph's rendition for everybody, repeatedly. The
/// person who turned it has already told the file. Sharing carries only what the
/// file would not take.</para>
/// </remarks>
public sealed class MergedTurns
{
    private readonly IDecisionReader _decisions;
    private readonly IDecisionRepository _repository;
    private readonly IRenditionTurner _renditions;
    private readonly IFaceRepository _faces;

    public MergedTurns(
        IDecisionReader decisions,
        IDecisionRepository repository,
        IRenditionTurner renditions,
        IFaceRepository faces)
    {
        _decisions = decisions;
        _repository = repository;
        _renditions = renditions;
        _faces = faces;
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
    public async Task<MergePlan> CarryOutAsync(
        MergePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Turns.Count == 0)
        {
            return plan;
        }

        IReadOnlyList<TurnTarget> targets = await _decisions
            .TurnTargetsAsync([.. plan.Turns.Select(turn => turn.Photo)], cancellationToken)
            .ConfigureAwait(false);

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
    private static int Quarter(int degrees) => ((degrees % 360) + 360) % 360;
}
