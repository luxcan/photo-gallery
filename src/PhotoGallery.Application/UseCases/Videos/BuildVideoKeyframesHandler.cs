using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Application.UseCases.Videos;

/// <summary>
/// Gives every video a poster, and the frames the face pass will read.
/// </summary>
/// <remarks>
/// The long one, and the reason [08] waited: 4,743 files holding 267 GB, which
/// is 91% of this library's bytes. It does not read them through - it opens each
/// container, asks how long it is, and seeks to a few points - so it costs far
/// less than the 11.6 hours a full read of them would. How much less depends on
/// the containers and has not been measured; the UI should say so rather than
/// promise a number.
///
/// <para>Shaped like the preparing pass, because it has the same three problems:
/// it runs for a long time, it can be stopped, and what it finished must survive
/// that. Work is batched, a batch is read by several threads and written by one,
/// and the write happens in a <c>finally</c>.</para>
///
/// <para>What is outstanding is decided by the disk rather than by the row, as
/// everywhere else here - and specifically by whether the poster is on disk. That
/// works because the poster is written <em>last</em>: see
/// <see cref="SaveFramesAsync"/>. A poster present therefore means the whole clip
/// was got through, and a clip interrupted half way leaves no poster and is
/// simply done again.</para>
/// </remarks>
public sealed class BuildVideoKeyframesHandler
{
    /// <summary>Videos prepared before their rows are written.</summary>
    /// <remarks>
    /// Smaller than the preparing pass's twenty. A video costs several seeks and
    /// several decodes where a photograph costs one read, so a batch represents
    /// much more work and losing one to an interruption is worth more.
    /// </remarks>
    private const int SaveBatchSize = 8;

    /// <summary>How many videos pass between progress reports.</summary>
    /// <remarks>
    /// Ten, against the other passes' twenty-five, because a video costs far
    /// more than a photograph does: a quarter of the reports for work that takes
    /// many times as long would leave the bar looking stuck.
    /// </remarks>
    private const int ReportEvery = 10;

    /// <summary>How many videos are opened at once when the caller has no opinion.</summary>
    /// <remarks>
    /// Not measured, unlike the numbers the other passes carry - there is no
    /// figure for video seeking on this library yet. Four is a deliberate
    /// halfway house: a seek over the share waits on the network like the
    /// preparing pass's eight, but the decode that follows competes for cores
    /// like the face pass's half-of-them. Worth replacing with a measurement.
    /// </remarks>
    public const int DefaultParallelism = 4;

    private readonly IGalleryReader _reader;
    private readonly IAssetRepository _assets;
    private readonly IThumbnailStore _store;
    private readonly IKeyframeExtractor _extractor;

    public BuildVideoKeyframesHandler(
        IGalleryReader reader,
        IAssetRepository assets,
        IThumbnailStore store,
        IKeyframeExtractor extractor)
    {
        _reader = reader;
        _assets = assets;
        _store = store;
        _extractor = extractor;
    }

    public async Task<VideoBuildResult> HandleAsync(
        int degreeOfParallelism = DefaultParallelism,
        IProgress<VideoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<PendingVideo> pending;
        try
        {
            pending = await FindPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped during the opening sweep. Answered rather than thrown,
            // because every other way out of this method is an answer: the query
            // that takes the token is the only thing here that raises, and a
            // caller that gets an exception for pressing Stop has to catch a
            // cancellation it did not know this pass could produce.
            return new VideoBuildResult(0, 0, 0, 0, stopwatch.Elapsed, Cancelled: true);
        }

        if (pending.Count == 0)
        {
            return new VideoBuildResult(0, 0, 0, 0, stopwatch.Elapsed, false);
        }

        int prepared = 0, failed = 0, skipped = 0, done = 0;
        bool cancelled = false;

        // Reported before any work, so the screen shows how many videos it is
        // about to get through rather than sitting on "starting..." through the
        // first ten of them - which on this pass is minutes, not moments.
        progress?.Report(new VideoProgress(0, pending.Count, 0, 0, stopwatch.Elapsed));

        try
        {
            foreach (PendingVideo[] batch in pending.Chunk(SaveBatchSize))
            {
                var completed = new ConcurrentQueue<VideoKeyframeUpdate>();
                var unreadable = new ConcurrentQueue<int>();

                try
                {
                    await Parallel.ForEachAsync(
                        batch,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = degreeOfParallelism > 0
                                ? degreeOfParallelism
                                : DefaultParallelism,
                            CancellationToken = cancellationToken,
                        },
                        async (item, token) =>
                        {
                            KeyframeReading reading = await _extractor
                                .ExtractAsync(item.FullPath, token)
                                .ConfigureAwait(false);

                            ExtractedVideo? extracted =
                                reading.Outcome == KeyframeOutcome.Extracted
                                && reading.Video is { Keyframes.Count: > 0 }
                                    ? reading.Video
                                    : null;

                            if (extracted is null)
                            {
                                // Only a settled answer is written down. A file
                                // that merely could not be reached is left alone
                                // and offered again next run - recording it as
                                // undecodable would leave the clip blank for
                                // good the moment the share came back, which is
                                // exactly what this pass used to do to one video
                                // in twenty.
                                if (reading.Outcome == KeyframeOutcome.Undecodable)
                                {
                                    unreadable.Enqueue(item.AssetId);
                                    Interlocked.Increment(ref failed);
                                }
                                else
                                {
                                    Interlocked.Increment(ref skipped);
                                }
                            }
                            else
                            {
                                IReadOnlyList<StoredKeyframe> stored = await SaveFramesAsync(
                                    item, extracted, token).ConfigureAwait(false);

                                completed.Enqueue(new VideoKeyframeUpdate(
                                    item.AssetId,
                                    extracted.Duration,
                                    extracted.SourceWidth,
                                    extracted.SourceHeight,
                                    stored));

                                Interlocked.Increment(ref prepared);
                            }

                            int seen = Interlocked.Increment(ref done);
                            if (seen % ReportEvery == 0)
                            {
                                progress?.Report(new VideoProgress(
                                    seen,
                                    pending.Count,
                                    Volatile.Read(ref prepared),
                                    Volatile.Read(ref failed),
                                    stopwatch.Elapsed));
                            }
                        }).ConfigureAwait(false);
                }
                finally
                {
                    // In a finally, so a batch interrupted part way still records
                    // the clips it did finish. Their frames are already on disk;
                    // a row that did not name them would have the next pass seek
                    // through those videos all over again.
                    await SaveAsync(completed, unreadable).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        progress?.Report(
            new VideoProgress(done, pending.Count, prepared, failed, stopwatch.Elapsed));
        return new VideoBuildResult(
            pending.Count, prepared, failed, skipped, stopwatch.Elapsed, cancelled);
    }

    /// <summary>
    /// Writes a clip's frames into the thumbnail store, poster last.
    /// </summary>
    /// <remarks>
    /// The order is the whole of this pass's resumability. The poster is what
    /// <see cref="FindPendingAsync"/> looks for on disk, so writing it first
    /// would let a clip interrupted between its frames look complete forever,
    /// with the middle and end of it never scanned for faces.
    ///
    /// <para>Each frame goes through <see cref="IThumbnailStore"/> unchanged, as
    /// a photograph's renditions do. The store names a file after the content
    /// hash it is handed, so handing it this frame's derived identity is what
    /// puts the frame where <see cref="VideoKeyframeIdentity"/> says it will
    /// be.</para>
    /// </remarks>
    private async Task<IReadOnlyList<StoredKeyframe>> SaveFramesAsync(
        PendingVideo item, ExtractedVideo extracted, CancellationToken cancellationToken)
    {
        var stored = new StoredKeyframe[extracted.Keyframes.Count];

        for (int ordinal = extracted.Keyframes.Count - 1; ordinal >= 0; ordinal--)
        {
            ExtractedKeyframe frame = extracted.Keyframes[ordinal];
            string identity = VideoKeyframeIdentity.For(
                item.RelativePath, item.Length, item.ModifiedUtc, ordinal);

            string name = await _store.SaveAsync(
                new GeneratedThumbnail(
                    frame.Tile,
                    frame.Preview,
                    extracted.SourceWidth,
                    extracted.SourceHeight,
                    TakenUtc: null,
                    PerceptualHash: default,
                    ContentHash: identity),
                cancellationToken).ConfigureAwait(false);

            stored[ordinal] = new StoredKeyframe(ordinal, frame.Position, name);
        }

        return stored;
    }

    /// <summary>
    /// Videos whose poster is not on disk in full, whatever their row claims.
    /// </summary>
    /// <remarks>
    /// Both renditions are asked about, not just the tile. The store writes a
    /// tile before its preview, so a run killed between those two writes leaves
    /// a poster that satisfies <see cref="IThumbnailStore.Exists"/> while the
    /// preview the face pass reads is not there - and the face pass, finding no
    /// preview, would quietly leave that clip alone for ever. Two stats instead
    /// of one, once per video, closes it.
    /// </remarks>
    private async Task<IReadOnlyList<PendingVideo>> FindPendingAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PendingVideo> candidates =
            await _reader.GetVideoCandidatesAsync(cancellationToken).ConfigureAwait(false);

        return [.. candidates.Where(NeedsFrames)];
    }

    private bool NeedsFrames(PendingVideo video)
    {
        string poster = PosterNameOf(video);
        return !_store.Exists(poster) || _store.PreviewWrittenUtc(poster) is null;
    }

    /// <summary>
    /// What this video's poster would be called, worked out without opening it.
    /// </summary>
    private string PosterNameOf(PendingVideo video) =>
        _store.NameFor(VideoKeyframeIdentity.For(
            video.RelativePath, video.Length, video.ModifiedUtc, ordinal: 0));

    private async Task SaveAsync(
        ConcurrentQueue<VideoKeyframeUpdate> completed, ConcurrentQueue<int> unreadable)
    {
        var batch = new List<VideoKeyframeUpdate>(completed.Count);
        while (completed.TryDequeue(out VideoKeyframeUpdate? update))
        {
            batch.Add(update);
        }

        if (batch.Count > 0)
        {
            // Not cancellable: these frames are already on disk, and a row that
            // did not record them would have the next pass redo the work.
            await _assets.UpdateVideoKeyframesAsync(batch, CancellationToken.None)
                .ConfigureAwait(false);
        }

        var failures = new List<int>(unreadable.Count);
        while (unreadable.TryDequeue(out int assetId))
        {
            failures.Add(assetId);
        }

        if (failures.Count > 0)
        {
            await _assets.MarkFailedAsync(failures, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
