using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Search;

namespace PhotoGallery.Application.UseCases.Search;

/// <summary>
/// Works out what every photograph is of, so that typing a description can find
/// it.
/// </summary>
/// <remarks>
/// Reads the cached previews and nothing else, so it touches no network however
/// far away the originals live - the same bargain the faces pass makes.
///
/// <para>Shaped like that pass too, because it has the same problem: it runs for
/// the best part of an hour, it can be stopped, and what it has finished must
/// survive that. Work is batched, each batch is read by several threads at once
/// and written by one, and the write happens in a <c>finally</c> so an
/// interrupted batch still records the pictures it got through.</para>
/// </remarks>
public sealed class IndexContentHandler
{
    /// <summary>
    /// How many previews are described before their vectors are written.
    /// </summary>
    /// <remarks>
    /// Small on purpose, as in the other passes. It buys no efficiency; all it
    /// decides is how much an interrupted run has to do again - and at three
    /// hundred milliseconds a picture, twenty is six seconds of lost work rather
    /// than an hour of it.
    /// </remarks>
    private const int SaveBatchSize = 20;

    /// <summary>
    /// How many pictures pass between progress reports.
    /// </summary>
    /// <remarks>
    /// Ten rather than the faces pass's twenty-five, because a picture here
    /// takes about three hundred milliseconds against that pass's hundred and
    /// forty. Twenty-five would leave the bar still for seven seconds at a time,
    /// which on an hour-long run reads as a bar that has stopped.
    /// </remarks>
    private const int ReportEvery = 10;

    /// <summary>
    /// How many previews are described at once when the caller has no opinion.
    /// </summary>
    /// <remarks>
    /// The same rule as the faces pass, and for the same reason: each graph is
    /// pinned to one thread, so this number is the whole of the parallelism, and
    /// half the cores leaves the machine usable while an hour-long pass runs.
    /// </remarks>
    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount / 2, 2, 12);

    private readonly IGalleryReader _reader;
    private readonly IThumbnailStore _store;
    private readonly IContentEncoder _encoder;
    private readonly IContentRepository _content;
    private readonly IModelStore _models;

    public IndexContentHandler(
        IGalleryReader reader,
        IThumbnailStore store,
        IContentEncoder encoder,
        IContentRepository content,
        IModelStore models)
    {
        _reader = reader;
        _store = store;
        _encoder = encoder;
        _content = content;
        _models = models;
    }

    public async Task<ContentIndexResult> HandleAsync(
        int degreeOfParallelism = 0,
        IProgress<ContentIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // What is outstanding first, and only then whether the models are here.
        // The order matters because this now runs at the end of every scan:
        // verifying the models means digesting 1.7 GB of weights, and a scan
        // that found nothing new must not pay that to discover it had nothing
        // to do. One indexed query against nothing at all.
        IReadOnlyList<PendingContentScan> pending;
        try
        {
            pending = await FindPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped during the opening query, which takes the token. Answered
            // rather than thrown, as every other way out of this method is.
            return new ContentIndexResult(
                0, 0, 0, stopwatch.Elapsed, Cancelled: true, ModelsMissing: false);
        }

        if (pending.Count == 0)
        {
            return new ContentIndexResult(0, 0, 0, stopwatch.Elapsed, false, false);
        }

        if (!Installed())
        {
            // Checked once, before a single preview is read: the alternative is
            // discovering it eleven thousand times over.
            return ContentIndexResult.WithoutModels(stopwatch.Elapsed);
        }

        int described = 0, failed = 0, done = 0;
        bool cancelled = false;

        // Reported before any work, so the screen names this phase the moment it
        // begins. Without it a short run - which after the first pass is every
        // run - would finish before the first periodic report and the phase
        // would only ever appear as it ended.
        progress?.Report(new ContentIndexProgress(0, pending.Count, 0, 0, stopwatch.Elapsed));

        try
        {
            foreach (PendingContentScan[] batch in pending.Chunk(SaveBatchSize))
            {
                var finished = new ConcurrentQueue<ContentScanUpdate>();

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
                            ContentEmbedding? vector = await _encoder
                                .DescribePictureAsync(item.PreviewPath, token)
                                .ConfigureAwait(false);

                            if (vector is not ContentEmbedding described_)
                            {
                                // Left unmarked rather than recorded as failed.
                                // A preview is this app's own file and the
                                // preparing pass can make it again, so the next
                                // run should try once more.
                                Interlocked.Increment(ref failed);
                            }
                            else
                            {
                                finished.Enqueue(new ContentScanUpdate(
                                    item.ThumbnailName,
                                    item.AssetIds,
                                    described_,
                                    DateTime.UtcNow));

                                Interlocked.Add(ref described, item.AssetIds.Count);
                            }

                            int seen = Interlocked.Increment(ref done);
                            if (seen % ReportEvery == 0)
                            {
                                progress?.Report(new ContentIndexProgress(
                                    seen, pending.Count, described, failed, stopwatch.Elapsed));
                            }
                        }).ConfigureAwait(false);
                }
                finally
                {
                    // In a finally so a batch stopped part way keeps what it
                    // finished. Written from this one thread rather than from the
                    // workers: SQLite tolerates concurrent readers, not
                    // concurrent writers.
                    await SaveAsync(finished).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        progress?.Report(
            new ContentIndexProgress(done, pending.Count, described, failed, stopwatch.Elapsed));

        return new ContentIndexResult(
            pending.Count, described, failed, stopwatch.Elapsed, cancelled, false);
    }

    private bool Installed() =>
        FeatureModels.Of(ModelFeature.ContentSearch)
            .All(id => _models.StateOf(id) == ModelState.Ready);

    /// <summary>
    /// The previews still to read, one entry per distinct rendition.
    /// </summary>
    private async Task<IReadOnlyList<PendingContentScan>> FindPendingAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ContentScanCandidate> candidates =
            await _reader.GetContentCandidatesAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. candidates
                .Where(candidate => _store.Exists(candidate.ThumbnailName))
                .GroupBy(candidate => candidate.ThumbnailName, StringComparer.OrdinalIgnoreCase)
                .Select(shared => new PendingContentScan(
                    _store.ResolvePreviewPath(shared.Key),
                    shared.Key,
                    [.. shared.Select(candidate => candidate.AssetId)])),
        ];
    }

    private async Task SaveAsync(ConcurrentQueue<ContentScanUpdate> finished)
    {
        var batch = new List<ContentScanUpdate>(finished.Count);
        while (finished.TryDequeue(out ContentScanUpdate? update))
        {
            batch.Add(update);
        }

        if (batch.Count > 0)
        {
            // Not cancellable: this work has been done, and a row that did not
            // record it would have the next pass do it again.
            await _content.SaveAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
