using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Takes the cached pictures the other machines have already made, and leaves
/// this library's for the ones that follow.
/// </summary>
/// <remarks>
/// The second half of the feature, and the reason a new laptop is usable the
/// same evening rather than the following week. On this library: roughly an hour
/// of reading 24.8 GB at 6.4 MB/s, plus the decode, replaced by about five
/// minutes of copying - and the photographs themselves never opened.
///
/// <para><strong>No original is ever sent, requested or received.</strong> Not
/// by request, not as an advanced option. What moves is the 400px tile and the
/// 1024px preview, both of which this app made itself.</para>
///
/// <para><strong>Idempotent, resumable and stoppable</strong>, like every other
/// pass. Copying is "take the names I do not have", so running it twice copies
/// nothing the second time and a run stopped halfway leaves every file it
/// finished usable.</para>
/// </remarks>
public sealed class ShareRenditionsHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionRepository _repository;
    private readonly IRenditionPool _pool;
    private readonly IThumbnailStore _thumbnails;

    public ShareRenditionsHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionRepository repository,
        IRenditionPool pool,
        IThumbnailStore thumbnails)
    {
        _index = index;
        _decisions = decisions;
        _repository = repository;
        _pool = pool;
        _thumbnails = thumbnails;
    }

    public async Task<PoolResult> HandleAsync(
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Asked uncancelled: this is one small query, and a token that is
        // already cancelled should yield a stopped result rather than throwing
        // out of the handler before it can report anything at all.
        ExchangeReadiness readiness =
            await _pool.ReadinessAsync(CancellationToken.None).ConfigureAwait(false);

        if (!readiness.CanExchange)
        {
            return PoolResult.CouldNot(readiness.Problem);
        }

        int offered = 0;

        try
        {
            MachineIdentity machine = await PublishDecisionsHandler
                .ThisMachineAsync(_index, cancellationToken)
                .ConfigureAwait(false);

            // Published first, and whole. A machine that copied pictures without
            // saying what its decode learned would leave the others with a
            // library that has no timeline, no places and nothing to cluster
            // occasions from.
            PreparedSet mine =
                await _decisions.PreparedAsync(machine, cancellationToken).ConfigureAwait(false);

            await _pool.PublishAsync(mine, cancellationToken).ConfigureAwait(false);

            IReadOnlyCollection<string> pooled =
                await _pool.NamesAsync(cancellationToken).ConfigureAwait(false);

            offered = await OfferAsync(pooled, progress, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return new PoolResult(true, string.Empty, 0, 0, offered, 0, WasCancelled: true);
            }

            (int filled, int fetched, int mismatched, bool stopped) =
                await TakeAsync(pooled, progress, cancellationToken).ConfigureAwait(false);

            return new PoolResult(
                true, string.Empty, filled, fetched, offered, mismatched, stopped);
        }
        catch (OperationCanceledException)
        {
            // Stopped, and reported as a stop rather than thrown at the caller.
            // Every file that finished copying is a file this machine keeps, and
            // the next run picks up from there - the same bargain every other
            // long pass in this app offers.
            return new PoolResult(true, string.Empty, 0, 0, offered, 0, WasCancelled: true);
        }
    }

    /// <summary>
    /// Copies this library's pictures into the pool, for the machines that have
    /// not made them yet.
    /// </summary>
    private async Task<int> OfferAsync(
        IReadOnlyCollection<string> pooled,
        IProgress<MergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PooledRendition> mine =
            await _decisions.RenditionsAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyCollection<string> offerable = RenditionMatching.Offerable(mine, pooled);

        if (offerable.Count == 0)
        {
            return 0;
        }

        int done = 0;
        int offered = 0;

        foreach (string name in offerable)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            progress?.Report(new MergeProgress("Sharing pictures", done++, offerable.Count));

            if (await _pool
                    .PushAsync(
                        name,
                        _thumbnails.ResolveTilePath(name),
                        _thumbnails.ResolvePreviewPath(name),
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                offered++;
            }
        }

        return offered;
    }

    /// <summary>
    /// Fills in this library's unprepared photographs from what the others have
    /// already worked out.
    /// </summary>
    /// <remarks>
    /// The pictures land before the rows are written, and that order is the
    /// whole of the safety here: a row marked ready is a row the gallery will
    /// draw, and one claiming a rendition that has not arrived is a tile it
    /// cannot.
    /// </remarks>
    private async Task<(int Filled, int Fetched, int Mismatched, bool Stopped)> TakeAsync(
        IReadOnlyCollection<string> pooled,
        IProgress<MergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PreparedSet> theirs =
            await _pool.FetchAsync(cancellationToken).ConfigureAwait(false);

        if (theirs.Count == 0)
        {
            return (0, 0, 0, false);
        }

        IReadOnlyList<Unprepared> here =
            await _decisions.UnpreparedAsync(cancellationToken).ConfigureAwait(false);

        PoolPlan plan = RenditionMatching.Match(here, theirs, Held(pooled));

        if (plan.FillIn.Count == 0)
        {
            return (0, 0, plan.Mismatched, false);
        }

        HashSet<string> landed = new(StringComparer.OrdinalIgnoreCase);
        int done = 0;

        foreach (string name in plan.Wanted)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            progress?.Report(new MergeProgress("Taking pictures", done++, plan.Wanted.Count));

            if (await _pool
                    .PullAsync(
                        name,
                        _thumbnails.ResolveTilePath(name),
                        _thumbnails.ResolvePreviewPath(name),
                        cancellationToken)
                    .ConfigureAwait(false))
            {
                landed.Add(name);
            }
        }

        // Only the rows whose picture is actually on this disk now - either just
        // copied, or already here. A fact whose rendition did not arrive is left
        // for the next run, which is what makes a stop cost nothing but the rest
        // of this one.
        List<PreparedFact> ready =
        [
            .. plan.FillIn.Where(fact =>
                fact.ThumbnailName is not string name
                || landed.Contains(name)
                || _thumbnails.Exists(name)),
        ];

        int filled = await _repository
            .FillInAsync(ready, cancellationToken)
            .ConfigureAwait(false);

        return (filled, landed.Count, plan.Mismatched, cancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// The names this library already has, so that nothing is fetched twice.
    /// </summary>
    /// <remarks>
    /// Asked of the disk rather than of the index. A row can name a rendition
    /// whose file is gone - a working folder copied without its thumbnails, a
    /// tidy-up - and believing the row would skip exactly the fetch that would
    /// fix it.
    /// </remarks>
    private IReadOnlyCollection<string> Held(IReadOnlyCollection<string> pooled) =>
        pooled.Count == 0 ? [] : _thumbnails.ListStoredNames();
}
