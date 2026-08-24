using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Thumbnails;

/// <summary>
/// Builds the previews the gallery shows, for every photo that has none.
/// </summary>
/// <remarks>
/// This is the pass that actually costs something: every original has to be read
/// once. Three things keep it bearable.
///
/// Reads run in parallel, because on a network share each file is dominated by
/// round-trip latency rather than bandwidth - one at a time leaves the link
/// mostly idle. Only photos whose tile is genuinely missing are considered, so
/// the pass resumes where it stopped instead of starting over. And results are
/// written in batches as they finish rather than all at the end, so an hour-long
/// pass that is interrupted keeps what it has already done.
///
/// <para>What a photo needs is decided by the disk, not by the row. A working
/// folder can be copied, cleaned or synced without its index, and a name in the
/// database is only a claim - one library had 11,481 rows naming tiles that had
/// all been deleted.</para>
/// </remarks>
public sealed class BuildThumbnailsHandler
{
    /// <summary>
    /// How many photos are processed before their results are written.
    /// </summary>
    /// <remarks>
    /// Deliberately small. Rows are written one statement each whatever this
    /// number is, so it does not buy efficiency - all it decides is how soon a
    /// finished picture becomes visible to the gallery, and how much an
    /// interrupted pass has to redo. The index sustains around 700 writes a
    /// second, so there is nothing to be gained by hoarding them.
    /// </remarks>
    private const int SaveBatchSize = 20;

    private readonly IGalleryReader _reader;
    private readonly IAssetRepository _assets;
    private readonly IThumbnailStore _store;
    private readonly IThumbnailGenerator _generator;

    public BuildThumbnailsHandler(
        IGalleryReader reader,
        IAssetRepository assets,
        IThumbnailStore store,
        IThumbnailGenerator generator)
    {
        _reader = reader;
        _assets = assets;
        _store = store;
        _generator = generator;
    }

    public async Task<ThumbnailBuildResult> HandleAsync(
        int degreeOfParallelism = 8,
        IProgress<ThumbnailProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<PendingThumbnail> pending =
            await FindPendingAsync(cancellationToken).ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return new ThumbnailBuildResult(0, 0, 0, stopwatch.Elapsed, false);
        }

        int built = 0, failed = 0, done = 0;
        bool cancelled = false;

        try
        {
            foreach (PendingThumbnail[] batch in pending.Chunk(SaveBatchSize))
            {
                var completed = new ConcurrentQueue<ThumbnailUpdate>();
                var unreadable = new ConcurrentQueue<int>();

                try
                {
                    await Parallel.ForEachAsync(
                        batch,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = degreeOfParallelism,
                            CancellationToken = cancellationToken,
                        },
                        async (item, token) =>
                        {
                            // The turn the user asked for is reapplied here, so a
                            // photograph straightened once stays straight through
                            // every later pass.
                            GeneratedThumbnail? generated = await _generator
                                .GenerateAsync(item.FullPath, item.Rotation, token)
                                .ConfigureAwait(false);

                            if (generated is null)
                            {
                                // Recorded rather than simply counted, so this file
                                // is not opened again on every future pass.
                                unreadable.Enqueue(item.AssetId);
                                Interlocked.Increment(ref failed);
                            }
                            else
                            {
                                string name = await _store
                                    .SaveAsync(generated, token)
                                    .ConfigureAwait(false);

                                completed.Enqueue(new ThumbnailUpdate(
                                    item.AssetId,
                                    name,
                                    generated.SourceWidth,
                                    generated.SourceHeight,
                                    generated.PerceptualHash,
                                    generated.TakenUtc,
                                    generated.ContentHash,
                                    generated.Latitude,
                                    generated.Longitude));
                                Interlocked.Increment(ref built);
                            }

                            int seen = Interlocked.Increment(ref done);
                            if (seen % 25 == 0)
                            {
                                progress?.Report(
                                    new ThumbnailProgress(seen, pending.Count, built, failed));
                            }
                        }).ConfigureAwait(false);
                }
                finally
                {
                    // In a finally, so a batch interrupted part way still records
                    // the pictures it did finish. Their renditions are already on
                    // disk; a row that did not name them would have the next pass
                    // read those originals all over again.
                    //
                    // Written from this one thread rather than from the workers:
                    // SQLite tolerates concurrent readers, not concurrent writers.
                    await SaveAsync(completed, unreadable).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        progress?.Report(new ThumbnailProgress(done, pending.Count, built, failed));
        return new ThumbnailBuildResult(pending.Count, built, failed, stopwatch.Elapsed, cancelled);
    }

    /// <summary>
    /// Photos with no tile on disk, whatever their row claims.
    /// </summary>
    private async Task<IReadOnlyList<PendingThumbnail>> FindPendingAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PendingThumbnail> candidates =
            await _reader.GetThumbnailCandidatesAsync(cancellationToken).ConfigureAwait(false);

        return [.. candidates.Where(candidate => !_store.Exists(candidate.ThumbnailName))];
    }

    private async Task SaveAsync(
        ConcurrentQueue<ThumbnailUpdate> completed, ConcurrentQueue<int> unreadable)
    {
        var batch = new List<ThumbnailUpdate>(completed.Count);
        while (completed.TryDequeue(out ThumbnailUpdate update))
        {
            batch.Add(update);
        }

        if (batch.Count > 0)
        {
            // Not cancellable: these files are already on disk, and a row that
            // does not record them would have the next pass redo the work.
            await _assets.UpdateThumbnailsAsync(batch, CancellationToken.None)
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
