using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Albums;
using PhotoGallery.Application.UseCases.Faces;
using PhotoGallery.Application.UseCases.Places;
using PhotoGallery.Application.UseCases.Scanning;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Application.UseCases.Thumbnails;
using PhotoGallery.Application.UseCases.Videos;

namespace PhotoGallery.Application.UseCases.Refresh;

/// <summary>
/// Everything importing means, in one action: crawl the folders and reconcile
/// them against the index, make the renditions that are missing, work out where
/// the photographs were taken, describe what they are of, take a picture out of
/// each video, and find the faces in all of it.
/// </summary>
/// <remarks>
/// Six phases rather than six buttons. Each of these was once its own button,
/// and between them they made using this app a procedure somebody had to
/// remember: scan, then find faces, then find where the photos were taken. Miss
/// a step and the library was quietly half made - videos with no picture on
/// them, photographs with no faces boxed, no places to search by - with nothing
/// on screen to say which step had been missed. None of these phases leaves the
/// library in a state anyone wants on its own, so none of them is a choice.
///
/// <para><strong>Expense is not a reason to split the job.</strong> The video
/// and face phases are the long ones, and each was defended as its own button on
/// exactly that ground. That is the app's own business: the answer to a phase
/// taking an hour is to name it on screen, show what is left, and let it be
/// stopped - not to make the user press a second button to finish the first.
/// </para>
///
/// <para><strong>The order is dependency first, then cost.</strong> Faces read
/// what the generating and video phases write, so they come after both. Beyond
/// that the cheap phases go first, so that a stop costs as little as possible:
/// everything before the phase being stopped is already saved, and the phase
/// itself resumes.</para>
///
/// <para><strong>An optional part missing must not look like a broken
/// scan.</strong> Describing and finding faces need models that may not be
/// installed, and placing photographs needs the sources reachable. All three
/// answer with a result that says so rather than raising, so the scan finishes
/// and reports which part of it could not run. Installing the models later needs
/// no new button: scanning again picks up everything that was skipped.</para>
///
/// <para><strong>Every phase after the crawl is library-wide, not
/// folder-wide.</strong> None of the candidate queries filters by source, so
/// scanning one small folder can set off outstanding work everywhere. That is
/// deliberate - work left half done should be finished by the next run rather
/// than waiting for the folder it belongs to - but it does mean a small scan is
/// not necessarily a short one.</para>
///
/// <para>The phases are deliberately asymmetric about progress. A crawl cannot
/// know how many files it will find until it has found them, so it reports a
/// running count against no total. Generating knows exactly how much work it has
/// the moment the crawl ends, which is the point at which the bar can start
/// filling.</para>
///
/// <para>Stopping is safe at any moment because the work is recorded as it goes:
/// rows are written in batches during the crawl, renditions in batches as they
/// are made, video frames written poster-last so a clip is either whole or not
/// started, each photograph's place settled one at a time, and everything not
/// yet done keeps its pending status. Running this again resumes rather than
/// starting over.</para>
/// </remarks>
public sealed class RefreshLibraryHandler
{
    /// <remarks>
    /// Eight at once. A network share is latency-bound, so one file at a time
    /// leaves the link mostly idle.
    /// </remarks>
    private const int GenerateParallelism = 8;

    private readonly ScanPhotoSourceHandler _scan;
    private readonly BuildThumbnailsHandler _generate;
    private readonly IndexContentHandler _describe;
    private readonly LocatePhotosHandler _locate;
    private readonly BuildVideoKeyframesHandler _videos;
    private readonly DetectFacesHandler _faces;
    private readonly ApplyHeldDecisionsHandler _waiting;
    private readonly BuildAlbumsHandler _collect;

    public RefreshLibraryHandler(
        ScanPhotoSourceHandler scan,
        BuildThumbnailsHandler generate,
        IndexContentHandler describe,
        LocatePhotosHandler locate,
        BuildVideoKeyframesHandler videos,
        DetectFacesHandler faces,
        ApplyHeldDecisionsHandler waiting,
        BuildAlbumsHandler collect)
    {
        _scan = scan;
        _generate = generate;
        _describe = describe;
        _locate = locate;
        _videos = videos;
        _faces = faces;
        _waiting = waiting;
        _collect = collect;
    }

    public async Task<RefreshResult> HandleAsync(
        IReadOnlyList<int> photoSourceIds,
        IProgress<RefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photoSourceIds);

        var stopwatch = Stopwatch.StartNew();
        var scans = new List<ScanResult>(photoSourceIds.Count);
        bool cancelled = false;

        foreach (int photoSourceId in photoSourceIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            // The folder being walked, where the walk has reached one, and the
            // source itself before that. A crawl cannot say how far along it is,
            // so the folder name is the only honest sign of movement it has.
            var crawl = new PhaseProgress<ScanProgress>(
                p => new RefreshProgress(
                    RefreshPhase.Indexing,
                    p.Folder.Length > 0 ? p.Folder : FolderName(p.SourcePath),
                    p.Seen,
                    0,
                    0),
                progress);

            ScanResult scan = await _scan
                .HandleAsync(photoSourceId, crawl, cancellationToken)
                .ConfigureAwait(false);

            scans.Add(scan);
            cancelled |= scan.WasCancelled;
        }

        if (cancelled)
        {
            // Generating from a half-finished crawl would work from an index that
            // is knowingly incomplete. Everything it would have made is still
            // pending, so the next run picks it up.
            stopwatch.Stop();
            return new RefreshResult(
                scans,
                Generated: null,
                Described: null,
                Located: null,
                Videos: null,
                Faces: null,
                Answers: null,
                Collected: null,
                stopwatch.Elapsed,
                WasCancelled: true);
        }

        // The total is knowable from here, so the bar can start filling.
        var making = new PhaseProgress<ThumbnailProgress>(
            p => new RefreshProgress(
                RefreshPhase.Generating, string.Empty, p.Done, p.Total, p.Failed),
            progress);

        ThumbnailBuildResult generated = await _generate
            .HandleAsync(GenerateParallelism, making, cancellationToken)
            .ConfigureAwait(false);

        if (generated.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new RefreshResult(
                scans,
                generated,
                Described: null,
                Located: null,
                Videos: null,
                Faces: null,
                Answers: null,
                Collected: null,
                stopwatch.Elapsed,
                WasCancelled: true);
        }

        // Where the photographs were taken, and first of the long phases because
        // it is the only one that goes back out to the sources for something it
        // cannot get anywhere else. The crawl and the generating phase have just
        // used the share, so this is the moment it is most likely to still be
        // there - and putting the network-bound work at the front means a stop
        // later costs none of it.
        //
        // It reads each original's header once and records the answer either way,
        // including the five in six that carry no coordinates, so a library
        // already placed passes in under a second.
        //
        // A source that cannot be reached is named in the result rather than
        // raised: an absent share must leave a scan reporting what it did manage,
        // not reporting failure.
        //
        // Reported once before it begins, as the two phases after it are. This
        // one opens by asking every source whether it can be reached, and an
        // absent share takes twenty-one seconds to answer - all of it with the
        // overlay still naming the phase before this one.
        progress?.Report(new RefreshProgress(RefreshPhase.Locating, string.Empty, 0, 0, 0));

        var locating = new PhaseProgress<LocatePhotosProgress>(
            p => new RefreshProgress(
                RefreshPhase.Locating, string.Empty, p.Done, p.Total, 0, p.Remaining),
            progress);

        LocatePhotosResult located = await _locate
            .HandleAsync(progress: locating, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (located.Cancelled || cancellationToken.IsCancellationRequested)
        {
            // The token as well as the result, at every phase boundary. A pass
            // that found nothing outstanding answers "not cancelled" however
            // long ago Stop was pressed, and carrying on from there would hand
            // an already-cancelled token to the next phase's query.
            stopwatch.Stop();
            return new RefreshResult(
                scans,
                generated,
                Described: null,
                located,
                Videos: null,
                Faces: null,
                Answers: null,
                Collected: null,
                stopwatch.Elapsed,
                WasCancelled: true);
        }

        // Every picture in the library that has no description yet, not only
        // those in the sources just crawled - the same scope every phase here
        // works at, and for the same reason: what is outstanding is a fact about
        // the library rather than about the folder someone happened to press Scan
        // on. So scanning one folder finishes what an earlier run left, which is
        // usually wanted and is worth knowing about because on a library never
        // described before it is an hour.
        //
        // It costs one query when nothing is outstanding, and is skipped entirely
        // when the search models are not installed - the core action must not
        // depend on an optional feature.
        var describing = new PhaseProgress<ContentIndexProgress>(
            p => new RefreshProgress(
                RefreshPhase.Describing, string.Empty, p.Read, p.Total, p.Failed, p.Remaining),
            progress);

        ContentIndexResult described = await _describe
            .HandleAsync(progress: describing, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (described.Cancelled || cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new RefreshResult(
                scans,
                generated,
                described,
                located,
                Videos: null,
                Faces: null,
                Answers: null,
                Collected: null,
                stopwatch.Elapsed,
                WasCancelled: true);
        }

        // The videos last, because this is the phase measured in hours and so the
        // one somebody actually stops. Everything before it is written by now, so
        // stopping here costs only video work - which the next run resumes, since
        // each batch is saved in a finally and a clip's poster is named last.
        // Run earlier, the same Stop would leave the library never described.
        //
        // Reported once before it begins because it opens by sweeping the disk
        // for missing posters - thousands of stats on this library, saying
        // nothing while they run - and until that finishes the overlay would
        // otherwise still be showing the phase before.
        progress?.Report(new RefreshProgress(RefreshPhase.PreparingVideos, string.Empty, 0, 0, 0));

        var preparing = new PhaseProgress<VideoProgress>(
            p => new RefreshProgress(
                RefreshPhase.PreparingVideos,
                string.Empty,
                p.Done,
                p.Total,
                p.Failed,
                p.Remaining),
            progress);

        VideoBuildResult videos = await _videos
            .HandleAsync(progress: preparing, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (videos.Cancelled || cancellationToken.IsCancellationRequested)
        {
            // The token as well as the result. A stop landing during that opening
            // sweep is not seen by the pass - it finds nothing outstanding, says
            // so, and the run would otherwise report a clean finish for a scan
            // the user had just stopped.
            stopwatch.Stop();
            return new RefreshResult(
                scans,
                generated,
                described,
                located,
                videos,
                Faces: null,
                Answers: null,
                Collected: null,
                stopwatch.Elapsed,
                WasCancelled: true);
        }

        // Faces last, because this phase reads what every phase before it wrote:
        // a photograph's preview and, since the videos were folded in, a clip's
        // keyframes too. Run it before them and every face in every video would
        // wait for the following scan.
        //
        // It answers rather than throws when the weights are not installed, which
        // is what lets it sit inside the core action at all: somebody who has not
        // installed the face models still gets a scan that finishes and says
        // plainly which part of it could not run.
        progress?.Report(new RefreshProgress(RefreshPhase.FindingFaces, string.Empty, 0, 0, 0));

        var finding = new PhaseProgress<FaceDetectionProgress>(
            p => new RefreshProgress(
                RefreshPhase.FindingFaces, string.Empty, p.Done, p.Total, p.Failed),
            progress);

        FaceDetectionResult faces = await _faces
            .HandleAsync(progress: finding, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (faces.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new RefreshResult(
                scans,
                generated,
                described,
                located,
                videos,
                faces,
                Answers: null,
                Collected: null,
                stopwatch.Elapsed,
                WasCancelled: true);
        }

        // Answers that came from another machine about photographs this library
        // had not indexed when they arrived. Here because a held answer names a
        // face and the phase above is what found them: run it before, and every
        // name waits another whole scan. Run it after the occasions, and they
        // are named from people the library was one step away from knowing
        // about.
        //
        // Silent on a library nobody shares with, which is why it can sit in the
        // core action: with nothing waiting it is a single count.
        var applying = new PhaseProgress<MergeProgress>(
            p => new RefreshProgress(
                RefreshPhase.ApplyingAnswers, p.What, p.Done, p.Total, 0),
            progress);

        HeldResult answers = await _waiting
            .HandleAsync(applying, cancellationToken)
            .ConfigureAwait(false);

        if (answers.WasCancelled || cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new RefreshResult(
                scans,
                generated,
                described,
                located,
                videos,
                faces,
                answers,
                Collected: null,
                stopwatch.Elapsed,
                WasCancelled: true);
        }

        // Grouping into occasions last of all, because it reads what every
        // phase before it wrote and decodes nothing itself: capture dates from
        // generating, places from locating, and the names on faces from the
        // phase above. Dependency-last rather than cost-last - one sorted pass
        // over dates the index already holds is the cheapest thing in the run.
        progress?.Report(new RefreshProgress(RefreshPhase.Collecting, string.Empty, 0, 0, 0));

        var collecting = new PhaseProgress<AlbumsProgress>(
            p => new RefreshProgress(
                RefreshPhase.Collecting, string.Empty, p.Done, p.Total, 0),
            progress);

        AlbumsResult collected = await _collect
            .HandleAsync(progress: collecting, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        return new RefreshResult(
            scans,
            generated,
            described,
            located,
            videos,
            faces,
            answers,
            collected,
            stopwatch.Elapsed,
            collected.WasCancelled || cancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// Passes one phase's progress straight out as a
    /// <see cref="RefreshProgress"/>, on the thread that reported it.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Progress{T}"/>, which is what each phase used
    /// to be wrapped in. That type posts to the synchronisation context it was
    /// built on, so wrapping every phase put a second hop in front of the one the
    /// caller already has - and the reports then arrived out of order. Recorded
    /// over one refresh it came out as
    /// <c>Locating, PreparingVideos, Locating, PreparingVideos, FindingFaces,
    /// PreparingVideos</c>: a phase that had finished repainting the overlay of
    /// the phase that followed it. Two phases hid it; six do not.
    ///
    /// <para>Forwarding synchronously leaves the marshalling to the caller's own
    /// <see cref="Progress{T}"/>, which was built on the UI thread and is where
    /// that belongs.</para>
    /// </remarks>
    private sealed class PhaseProgress<T> : IProgress<T>
    {
        private readonly Func<T, RefreshProgress> _shape;
        private readonly IProgress<RefreshProgress>? _onward;

        public PhaseProgress(Func<T, RefreshProgress> shape, IProgress<RefreshProgress>? onward)
        {
            _shape = shape;
            _onward = onward;
        }

        public void Report(T value) => _onward?.Report(_shape(value));
    }

    /// <summary>The last segment, which is what identifies a folder on screen.</summary>
    private static string FolderName(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        int cut = trimmed.LastIndexOfAny(['\\', '/']);
        return cut < 0 ? trimmed : trimmed[(cut + 1)..];
    }
}
