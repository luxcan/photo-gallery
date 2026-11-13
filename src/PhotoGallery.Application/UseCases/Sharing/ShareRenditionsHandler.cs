using System.Diagnostics;
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
///
/// <para><strong>Two phases of the scan, not a button.</strong> It was a
/// button, shown only on a laptop that still had photographs to prepare and
/// worded as a take - so a laptop with everything prepared never gave, the
/// folder never held a picture, and no face ever travelled. Now a scan takes
/// before it makes anything, so that it does not spend an hour making what
/// another laptop has already made, and gives at its very end, once it has made
/// and found everything it is going to. Nobody has to remember either.</para>
///
/// <para><strong>The folder must never cost a scan.</strong> It is a drive that
/// sleeps, a laptop that has left the house, or nothing chosen at all - and a
/// scan is the app's main action. So each phase asks the folder first, against
/// a deadline, and goes on without it when it does not answer; a folder that
/// goes away part way ends the phase rather than the scan; and every outcome is
/// a result that says so, never an exception.</para>
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
    private readonly IDecisionExchange _exchange;
    private readonly SyncFolderProbe _folder;

    public ShareRenditionsHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionRepository repository,
        IRenditionPool pool,
        IThumbnailStore thumbnails,
        ApplyHeldDecisionsHandler waiting,
        IModelStore models,
        IDecisionExchange exchange,
        SyncFolderProbe folder)
    {
        _index = index;
        _decisions = decisions;
        _repository = repository;
        _pool = pool;
        _thumbnails = thumbnails;
        _waiting = waiting;
        _models = models;
        _exchange = exchange;
        _folder = folder;
    }

    /// <summary>
    /// Takes the pictures and faces the other machines have left, before this
    /// one makes any of its own.
    /// </summary>
    /// <remarks>
    /// A scan's phase, run straight after the crawl: what is taken here is a
    /// picture the generating phase does not make and a face the face phase
    /// does not look for. Run after them, it would only ever arrive in time to
    /// be thrown away.
    /// </remarks>
    public Task<PoolResult> TakeAsync(
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        TimedAsync(async () =>
        {
            if (await UnusableAsync(progress, cancellationToken).ConfigureAwait(false)
                is string problem)
            {
                return PoolResult.CouldNot($"Skipped, because {problem}.");
            }

            // Read once, for both halves: the pictures are taken from these, and
            // they are also what says which picture each machine's faces were
            // drawn on.
            IReadOnlyList<PreparedSet> theirs =
                await _pool.FetchAsync(cancellationToken).ConfigureAwait(false);

            (int filled, int fetched, int mismatched, string? gone) =
                await TakePicturesAsync(theirs, progress, cancellationToken).ConfigureAwait(false);

            if (gone is not null)
            {
                return PoolResult.CouldNot(PartWay(gone)) with
                {
                    Filled = filled,
                    Fetched = fetched,
                    Mismatched = mismatched,
                };
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PoolResult.Stopped with
                {
                    Filled = filled,
                    Fetched = fetched,
                    Mismatched = mismatched,
                };
            }

            (int faces, IReadOnlyList<ModelMismatch> refused) =
                await TakeFacesAsync(theirs, progress, cancellationToken).ConfigureAwait(false);

            // A turn merged before its picture arrived was held rather than
            // dropped, and this is the moment the picture arrives. The scan's own
            // sweep is hours away behind the long phases, and a stop before it
            // would leave a photograph the user straightened sitting sideways
            // until the next one - so this run, the one that knows a picture
            // landed, lands what was waiting for it.
            //
            // And a name merged before its face was found, for the same reason:
            // a laptop that already had every picture takes only faces, and the
            // names waiting on them are the whole of what those faces are for.
            HeldResult? swept = fetched > 0 || faces > 0
                ? await _waiting.HandleAsync(progress, cancellationToken).ConfigureAwait(false)
                : null;

            return new PoolResult(
                true, string.Empty, filled, fetched, 0, mismatched,
                cancellationToken.IsCancellationRequested, faces, refused)
            {
                Swept = swept,
            };
        });

    /// <summary>
    /// Leaves this library's pictures, what its decode learned and its faces,
    /// for the machines that follow.
    /// </summary>
    /// <remarks>
    /// A scan's last phase, after everything it gives has been made: the
    /// pictures by generating and the videos, the faces by the face phase, and
    /// the turns that decide which of them may be given by the answers that
    /// land after that. Last also because the first time it runs it is
    /// gigabytes, and this is the phase somebody stops - by then a stop costs
    /// nothing else.
    /// </remarks>
    public Task<PoolResult> GiveAsync(
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        TimedAsync(async () =>
        {
            if (await UnusableAsync(progress, cancellationToken).ConfigureAwait(false)
                is string problem)
            {
                return PoolResult.CouldNot($"Skipped, because {problem}.");
            }

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

            (int offered, string? gone) =
                await OfferAsync(progress, cancellationToken).ConfigureAwait(false);

            if (gone is not null)
            {
                return PoolResult.CouldNot(PartWay(gone)) with { Offered = offered };
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return PoolResult.Stopped with { Offered = offered };
            }

            (bool left, IReadOnlyList<string> behind, int unreadable) =
                await GiveFacesAsync(machine, progress, cancellationToken).ConfigureAwait(false);

            return PoolResult.Nothing with
            {
                Offered = offered,
                FacesLeft = left,
                FacesWaitFor = behind,
                FacesWaitForUnreadable = unreadable,
            };
        });

    /// <summary>
    /// Runs one phase, timed, and answers for every way it can end.
    /// </summary>
    /// <remarks>
    /// A stop is reported as a stop rather than thrown at the caller. Every file
    /// that finished copying is a file this machine keeps, and the next run
    /// picks up from there - the same bargain every other long pass in this app
    /// offers.
    ///
    /// <para>And a folder that fails part way is reported rather than raised. The
    /// folder is somewhere else, on a network that comes and goes, and a scan
    /// that had been running for an hour must not end in an error over a copy
    /// it could simply make again next time.</para>
    /// </remarks>
    private static async Task<PoolResult> TimedAsync(Func<Task<PoolResult>> phase)
    {
        var stopwatch = Stopwatch.StartNew();
        PoolResult result;

        try
        {
            result = await phase().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = PoolResult.Stopped;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result = PoolResult.CouldNot(
                PartWay($"the sync folder could not be used: {ex.Message.TrimEnd('.')}"));
        }

        return result with { Elapsed = stopwatch.Elapsed };
    }

    /// <summary>What a phase that lost its folder part way says about it.</summary>
    private static string PartWay(string problem) =>
        $"Stopped part way, because {problem}. What was copied is kept.";

    /// <summary>
    /// Why the folder cannot be used this time, or null when it can - asked
    /// against a deadline rather than waited on.
    /// </summary>
    /// <remarks>
    /// The same question, under the same deadline, that the Sync screen and its
    /// button ask: see <see cref="SyncFolderProbe"/>.
    /// </remarks>
    /// <param name="progress">
    /// Told the phase has begun once there is a folder to ask, and not before. A
    /// library nobody shares has nothing to do here, and a phase with nothing to
    /// do says nothing - as describing says nothing without its models.
    /// </param>
    private async Task<string?> UnusableAsync(
        IProgress<MergeProgress>? progress, CancellationToken cancellationToken)
    {
        SyncFolderAnswer answer =
            await _folder.AskAsync(progress, cancellationToken).ConfigureAwait(false);

        return answer.Reason;
    }

    /// <summary>
    /// Whether a copy that failed failed because the folder has gone, rather
    /// than over the one file.
    /// </summary>
    /// <remarks>
    /// One file failing is ordinary - another machine writing the same name at
    /// the same moment, a picture whose file is missing here - and the run
    /// carries on past it. A folder that has gone is not, and carrying on would
    /// spend tens of seconds failing on each of twenty thousand names: a scan
    /// that never ends, which is worse than one that stops.
    /// </remarks>
    private Task<string?> GoneAsync(CancellationToken cancellationToken) =>
        UnusableAsync(progress: null, cancellationToken);

    /// <summary>
    /// Copies this library's pictures into the pool, for the machines that have
    /// not made them yet.
    /// </summary>
    /// <remarks>
    /// Only the names the pool lacks, worked out from one listing of it before a
    /// byte moves - which is what makes every scan after the first copy only
    /// what is new.
    /// </remarks>
    private async Task<(int Offered, string? Gone)> OfferAsync(
        IProgress<MergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<string> pooled =
            await _pool.NamesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PooledRendition> mine =
            await _decisions.RenditionsAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyCollection<string> offerable = RenditionMatching.Offerable(mine, pooled);

        int done = 0;
        int offered = 0;

        foreach (string name in offerable)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            progress?.Report(new MergeProgress(
                "Pictures for your other computers", done++, offerable.Count));

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
            else if (await GoneAsync(cancellationToken).ConfigureAwait(false) is string gone)
            {
                return (offered, gone);
            }
        }

        return (offered, null);
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
    private async Task<(int Filled, int Fetched, int Mismatched, string? Gone)> TakePicturesAsync(
        IReadOnlyList<PreparedSet> theirs,
        IProgress<MergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (theirs.Count == 0)
        {
            return (0, 0, 0, null);
        }

        IReadOnlyList<Unprepared> here =
            await _decisions.UnpreparedAsync(cancellationToken).ConfigureAwait(false);

        // What this library already has is asked of the disk rather than of the
        // index, and only once there is something it could save. A row can name
        // a rendition whose file is gone - a working folder copied without its
        // thumbnails, a tidy-up - and believing the row would skip exactly the
        // fetch that would fix it.
        PoolPlan plan = here.Count == 0
            ? PoolPlan.Nothing
            : RenditionMatching.Match(here, theirs, _thumbnails.ListStoredNames());

        if (plan.FillIn.Count == 0)
        {
            return (0, 0, plan.Mismatched, null);
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
        string? gone = null;
        int done = 0;

        foreach (string name in wanted)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            progress?.Report(new MergeProgress(
                "Pictures made on your other computers", done++, wanted.Count));

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
            else if (await GoneAsync(cancellationToken).ConfigureAwait(false) is string away)
            {
                gone = away;
                break;
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

        return (filled, landed.Count, plan.Mismatched, gone);
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
    ///
    /// <para>Read after the pictures have landed, so that a photograph whose
    /// picture just arrived can take the faces drawn on it in the same run.</para>
    /// </remarks>
    /// <param name="drawnOn">
    /// The other machines' facts, which say which picture each of their faces
    /// was drawn on.
    /// </param>
    private async Task<(int Faces, IReadOnlyList<ModelMismatch> Refused)> TakeFacesAsync(
        IReadOnlyList<PreparedSet> drawnOn,
        IProgress<MergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FaceSet> theirs =
            await _pool.FetchFacesAsync(cancellationToken).ConfigureAwait(false);

        if (theirs.Count == 0)
        {
            return (0, []);
        }

        (IReadOnlyList<FaceSet> accepted, IReadOnlyList<ModelMismatch> refused) =
            VectorAcceptance.Sift(Fingerprints(), theirs);

        if (accepted.Count == 0)
        {
            return (0, refused);
        }

        LibraryContents here =
            await _decisions.ContentsAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SharedFace> landing =
            VectorAcceptance.Landing(accepted, OnThisDisk(here, accepted), drawnOn);

        if (landing.Count == 0)
        {
            return (0, refused);
        }

        progress?.Report(new MergeProgress(
            "Faces found on your other computers", 0, landing.Count));

        int added = await _repository
            .AddFacesAsync(landing, cancellationToken)
            .ConfigureAwait(false);

        return (added, refused);
    }

    /// <summary>
    /// The contents, with only the pictures that are actually on this disk.
    /// </summary>
    /// <remarks>
    /// Asked of the disk rather than believed from the row, as the pictures are:
    /// a row can name a picture whose files are gone, and the generating phase
    /// makes it again a moment later. The face pass then reads a preview newer
    /// than the faces taken now and replaces every one of them - after the names
    /// waiting for them had already been landed and let go.
    ///
    /// <para>Only the photographs some face is arriving for, so the cost is a
    /// stat for each of those rather than for the whole library.</para>
    /// </remarks>
    private LibraryContents OnThisDisk(LibraryContents here, IReadOnlyList<FaceSet> arriving)
    {
        HashSet<AssetKey> wanted =
            [.. arriving.SelectMany(set => set.Faces).Select(face => face.Face.Photo)];

        Dictionary<AssetKey, string> pictured = [];

        foreach ((AssetKey photo, string name) in here.Renditions)
        {
            if (wanted.Contains(photo) && _thumbnails.Exists(name))
            {
                pictured[photo] = name;
            }
        }

        return here with { Renditions = pictured };
    }

    /// <summary>
    /// Leaves this library's faces in the folder, once no other machine there
    /// would add them a second time.
    /// </summary>
    /// <remarks>
    /// <strong>Left even by a machine that has found no faces yet</strong>,
    /// because the file is what says which models it is running.
    ///
    /// <para><strong>Held back while any other machine is on the build
    /// before.</strong> That build lands a face as new unless its box matches one
    /// of its own to the pixel, and the detector runs on the graphics card, so
    /// the boxes two machines draw round one face differ by a pixel. Given these
    /// faces it would put a second face on nearly every face both machines
    /// found, silently. The pictures and the facts are given regardless - they
    /// do not depend on it - and a file already in the folder is left exactly as
    /// it is, because taking it away would not take back what was landed from
    /// it.</para>
    /// </remarks>
    private async Task<(bool Left, IReadOnlyList<string> Behind, int Unreadable)> GiveFacesAsync(
        MachineIdentity machine,
        IProgress<MergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        (IReadOnlyList<string> behind, int unreadable) =
            await BehindAsync(cancellationToken).ConfigureAwait(false);

        if (behind.Count > 0 || unreadable > 0)
        {
            return (false, behind, unreadable);
        }

        progress?.Report(new MergeProgress("Faces for your other computers", 0, 0));

        FaceSet mine = new(
            machine,
            DateTime.UtcNow,
            Fingerprints(),
            await _decisions.FacesAsync(cancellationToken).ConfigureAwait(false));

        bool left = await _pool.PublishFacesAsync(mine, cancellationToken).ConfigureAwait(false);

        return (left, [], 0);
    }

    /// <summary>
    /// The other machines that have not said they land faces by overlap, by the
    /// names they go by.
    /// </summary>
    /// <remarks>
    /// Read from the answers each one last left in the folder, because that is
    /// the only thing every build writes. A file that cannot be read this time
    /// cannot say it either, and counts against: holding the faces back for one
    /// more scan costs nothing, and the duplicates it would risk cannot be
    /// taken back.
    /// </remarks>
    private async Task<(IReadOnlyList<string> Named, int Unreadable)> BehindAsync(
        CancellationToken cancellationToken)
    {
        FetchedDecisions published =
            await _exchange.FetchAsync(cancellationToken).ConfigureAwait(false);

        return (
            [
                .. published.Sets
                    .Where(set => !set.LandsFacesByOverlap)
                    .Select(set => set.Machine.Name)
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .Order(StringComparer.CurrentCultureIgnoreCase),
            ],
            published.Unreadable.Count);
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
}
