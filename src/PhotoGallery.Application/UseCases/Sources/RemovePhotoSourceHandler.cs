using System.Diagnostics;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Sources;

/// <summary>
/// Detaches a photo source from the library. Indexed data for that source goes
/// with it, along with the cached copies it caused to be made; the user's own
/// files are never touched.
/// </summary>
/// <remarks>
/// Reclaiming the cache matters because it is the bulk of the working folder -
/// about 1.6 GB for this library. Leaving it behind would mean detaching a
/// folder quietly kept the disk it was using.
///
/// <para>Each record's files are deleted, and proved gone, before its row is
/// removed - never the other way round. Nothing spans a transaction here, since
/// SQLite cannot enrol a file delete, so the order is the whole guarantee. A
/// rendition missing while its row survives heals itself: the prepare pass
/// decides by what is on disk rather than by what the row claims, and rebuilds
/// it. A file surviving with no row is invisible forever. Every interruption
/// therefore lands on the recoverable side, and running the detach again
/// finishes it.</para>
///
/// <para>Names are shared: two sources holding the same picture point at the
/// same pair of files, because a rendition is named after the picture rather
/// than the row. So only names that nothing else still uses are deleted, and
/// that question is asked once, before anything is removed.</para>
/// </remarks>
public sealed class RemovePhotoSourceHandler
{
    /// <remarks>
    /// Rows go in batches rather than one at a time: sixteen thousand separate
    /// commits would cost far more than the file deletion they follow. Fifty is
    /// small enough that the window gets a few hundred chances to repaint over a
    /// library this size, which is what keeps the progress bar from looking hung.
    /// </remarks>
    private const int BatchSize = 50;

    private readonly ILibraryIndex _index;
    private readonly IAssetRepository _assets;
    private readonly IThumbnailStore _thumbnails;

    public RemovePhotoSourceHandler(
        ILibraryIndex index, IAssetRepository assets, IThumbnailStore thumbnails)
    {
        _index = index;
        _assets = assets;
        _thumbnails = thumbnails;
    }

    public async Task<RemovePhotoSourceResult> HandleAsync(
        int sourceId,
        IProgress<RemovePhotoSourceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Setup runs uncancelled, as scanning does: a token that is already
        // cancelled should yield a cancelled result rather than throwing out of
        // the handler before it can report anything.
        IReadOnlyList<AssetRendition> records = await _assets
            .ListRenditionsAsync(sourceId, CancellationToken.None)
            .ConfigureAwait(false);
        HashSet<string> keep = await _assets
            .GetThumbnailNamesExceptAsync(sourceId, CancellationToken.None)
            .ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        var pendingIds = new List<int>(BatchSize);
        int total = records.Count;
        int done = 0, removed = 0, reclaimed = 0, failed = 0;
        bool cancelled = false;

        // Every id in here has already lost its files, so the rows must follow
        // whatever happens next - which is why this is called after every batch
        // and again from the finally, and why it is not cancellable.
        async Task FlushAsync()
        {
            if (pendingIds.Count == 0)
            {
                return;
            }

            await _assets.RemoveAsync(pendingIds, CancellationToken.None).ConfigureAwait(false);
            removed += pendingIds.Count;
            pendingIds.Clear();
        }

        // Reported before any work, so the bar paints at nought rather than
        // appearing once the first batch is already behind it.
        progress?.Report(new RemovePhotoSourceProgress(0, total, 0, 0));

        try
        {
            foreach (AssetRendition[] batch in records.Chunk(BatchSize))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                // Deleting files is synchronous I/O. Running each batch off the
                // calling thread hands the window back between batches, so it
                // keeps painting and the Stop button stays clickable.
                BatchOutcome outcome = await Task
                    .Run(() => DeleteBatch(batch, keep, cancellationToken), CancellationToken.None)
                    .ConfigureAwait(false);

                pendingIds.AddRange(outcome.ClearedIds);
                reclaimed += outcome.Reclaimed;
                failed += outcome.Failed;
                done += outcome.ClearedIds.Count + outcome.Failed;

                await FlushAsync().ConfigureAwait(false);
                progress?.Report(new RemovePhotoSourceProgress(done, total, reclaimed, failed));

                if (outcome.Stopped)
                {
                    cancelled = true;
                    break;
                }
            }
        }
        finally
        {
            // The last word: a partial batch, or one interrupted by something
            // unexpected, has still lost its files, so its rows go too.
            await FlushAsync().ConfigureAwait(false);
        }

        bool detached = !cancelled && failed == 0 && removed == total;
        if (detached)
        {
            // Only now. Every file this source owned is off the disk, so nothing
            // is left for the row - or its cascade - to strand.
            await _index.RemoveSourceAsync(sourceId, CancellationToken.None).ConfigureAwait(false);
            reclaimed += await Task
                .Run(() => SweepOrphans(keep), CancellationToken.None)
                .ConfigureAwait(false);
        }

        stopwatch.Stop();
        progress?.Report(new RemovePhotoSourceProgress(done, total, reclaimed, failed));

        return new RemovePhotoSourceResult(
            removed, total, reclaimed, failed, stopwatch.Elapsed, cancelled);
    }

    private BatchOutcome DeleteBatch(
        IReadOnlyList<AssetRendition> batch,
        HashSet<string> keep,
        CancellationToken cancellationToken)
    {
        var cleared = new List<int>(batch.Count);
        int reclaimed = 0, failed = 0;

        foreach (AssetRendition record in batch)
        {
            // Asked between records and never inside one, so the record being
            // worked on always finishes and its files and row go together.
            if (cancellationToken.IsCancellationRequested)
            {
                return new BatchOutcome(cleared, reclaimed, failed, Stopped: true);
            }

            if (record.ThumbnailName is null || keep.Contains(record.ThumbnailName))
            {
                // Nothing of this record's to lose: either it was never prepared,
                // or another source still points at the same pair of files.
                cleared.Add(record.AssetId);
            }
            else if (_thumbnails.TryDelete(record.ThumbnailName))
            {
                reclaimed++;
                cleared.Add(record.AssetId);
            }
            else
            {
                // The row stays with its files. It is the only thing that still
                // names them, so removing it would strand them for good.
                failed++;
            }
        }

        return new BatchOutcome(cleared, reclaimed, failed, Stopped: false);
    }

    /// <summary>
    /// Deletes the renditions nothing references at all, then the directories
    /// left empty.
    /// </summary>
    /// <remarks>
    /// A picture whose bytes changed between two prepare passes was written under
    /// a new name, leaving its previous pair of files behind with no row naming
    /// them. Nothing else in the app can see those. A detach that has just
    /// finished is the moment to sweep them: the index is complete, and no other
    /// pass can be running because they lock each other out.
    /// </remarks>
    private int SweepOrphans(HashSet<string> stillUsed)
    {
        int swept = 0;
        foreach (string name in _thumbnails.ListStoredNames())
        {
            if (!stillUsed.Contains(name) && _thumbnails.TryDelete(name))
            {
                swept++;
            }
        }

        _thumbnails.RemoveEmptyShards();
        return swept;
    }

    /// <summary>What one batch of records managed to clear.</summary>
    private readonly record struct BatchOutcome(
        List<int> ClearedIds, int Reclaimed, int Failed, bool Stopped);
}
