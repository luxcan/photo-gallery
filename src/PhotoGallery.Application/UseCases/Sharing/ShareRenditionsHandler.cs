using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
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
    private readonly ApplyHeldDecisionsHandler _waiting;
    private readonly IModelStore _models;

    public ShareRenditionsHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionRepository repository,
        IRenditionPool pool,
        IThumbnailStore thumbnails,
        ApplyHeldDecisionsHandler waiting,
        IModelStore models)
    {
        _index = index;
        _decisions = decisions;
        _repository = repository;
        _pool = pool;
        _thumbnails = thumbnails;
        _waiting = waiting;
        _models = models;
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

            (int faces, IReadOnlyList<ModelMismatch> refused) =
                await VectorsAsync(machine, progress, cancellationToken).ConfigureAwait(false);

            // A turn merged before its picture arrived was held rather than
            // dropped, and this is the moment the picture arrives. Waiting for
            // the next scan would leave a photograph the user straightened
            // sitting sideways for however long that took - and this run is the
            // one that knows a picture landed.
            if (fetched > 0 && !stopped)
            {
                await _waiting
                    .HandleAsync(progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new PoolResult(
                true, string.Empty, filled, fetched, offered, mismatched, stopped,
                faces, refused);
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

        // A video's frame names are worked out here rather than looked up. The
        // digest is seeded from the path, the length, the modified time and the
        // ordinal - every one of which this machine's own crawl already knows -
        // so the manifest carries where each frame came from and nothing about
        // what it is called.
        HashSet<string> wanted = new(plan.Wanted, StringComparer.OrdinalIgnoreCase);

        foreach (PreparedFact fact in plan.FillIn)
        {
            foreach (string frame in Frames(fact))
            {
                if (!_thumbnails.Exists(frame))
                {
                    wanted.Add(frame);
                }
            }
        }

        HashSet<string> landed = new(StringComparer.OrdinalIgnoreCase);
        int done = 0;

        foreach (string name in wanted)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            progress?.Report(new MergeProgress("Taking pictures", done++, wanted.Count));

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
    /// Takes the faces the other machines have already found, where their models
    /// match.
    /// </summary>
    /// <remarks>
    /// Two hours of detection on this library, against seconds of copying - and
    /// the whole of it rests on the fingerprint check. An embedding is
    /// meaningless outside the model that produced it, and a mismatched one does
    /// not fail: it returns a confident answer about the wrong person, which
    /// looks exactly like a right one and would spread through a family's
    /// library as fast as the sharing that carried it.
    /// </remarks>
    private async Task<(int Faces, IReadOnlyList<ModelMismatch> Refused)> VectorsAsync(
        MachineIdentity machine,
        IProgress<MergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> mine = Fingerprints();

        // Published even by a machine that has found no faces yet, because the
        // file is what says which models it is running.
        await _pool
            .PublishFacesAsync(
                new FaceSet(
                    machine,
                    DateTime.UtcNow,
                    mine,
                    await _decisions.FacesAsync(cancellationToken).ConfigureAwait(false)),
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<FaceSet> theirs =
            await _pool.FetchFacesAsync(cancellationToken).ConfigureAwait(false);

        if (theirs.Count == 0)
        {
            return (0, []);
        }

        (IReadOnlyList<FaceSet> accepted, IReadOnlyList<ModelMismatch> refused) =
            VectorAcceptance.Sift(mine, theirs);

        if (accepted.Count == 0)
        {
            return (0, refused);
        }

        LibraryContents here =
            await _decisions.ContentsAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SharedFace> landing = VectorAcceptance.Landing(accepted, here);

        if (landing.Count == 0)
        {
            return (0, refused);
        }

        progress?.Report(new MergeProgress("Taking faces", 0, landing.Count));

        int added = await _repository
            .AddFacesAsync(landing, cancellationToken)
            .ConfigureAwait(false);

        return (added, refused);
    }

    /// <summary>
    /// This library's models, by name and by the digest of the file on disk.
    /// </summary>
    /// <remarks>
    /// Only the ones actually installed. A model this machine does not have is
    /// not a disagreement - it has no vectors of its own to contradict, and
    /// taking somebody else's is the entire point.
    /// </remarks>
    private Dictionary<string, string> Fingerprints()
    {
        Dictionary<string, string> models = [];

        foreach (ModelId id in new[] { ModelId.FaceDetection, ModelId.FaceRecognition })
        {
            if (_models.StateOf(id) == ModelState.Ready)
            {
                models[id.ToString()] = _models.Describe(id).Sha256;
            }
        }

        return models;
    }

    /// <summary>
    /// What a video's frames are called here, worked out rather than looked up.
    /// </summary>
    /// <remarks>
    /// A photograph's rendition is named after a hash of its bytes, which is
    /// exactly what the receiving machine is trying to avoid reading - so it has
    /// to be told. A video's frame is named from facts a scan collects for free,
    /// so being told would be carrying 4,743 clips' worth of names that this
    /// machine can derive in a millisecond.
    /// </remarks>
    private IEnumerable<string> Frames(PreparedFact fact)
    {
        foreach (SharedKeyframe still in fact.Keyframes)
        {
            yield return _thumbnails.NameFor(VideoKeyframeIdentity.For(
                fact.Photo.RelativePath, fact.Length, fact.ModifiedUtc, still.Ordinal));
        }
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
