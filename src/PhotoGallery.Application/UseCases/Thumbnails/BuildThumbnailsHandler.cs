using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sources;

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
///
/// <para>A source that cannot be reached is left alone, and so is a source that
/// drops while the pass is running. A file that would not open is written off as
/// unreadable only when its source still answers: a picture on a share that was
/// switched off is not broken, and writing it off kept it blank for good once
/// the share came back - a scan with the drive off did that to 13,990 of
/// them.</para>
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

    /// <summary>
    /// ERROR_SHARING_VIOLATION as the HResult of the IOException it is raised
    /// as: another program has the file open.
    /// </summary>
    private const int SharingViolation = unchecked((int)0x80070020);

    /// <summary>
    /// ERROR_LOCK_VIOLATION, the same: another program has part of the file
    /// locked.
    /// </summary>
    private const int LockViolation = unchecked((int)0x80070021);

    private readonly IGalleryReader _reader;
    private readonly IAssetRepository _assets;
    private readonly IThumbnailStore _store;
    private readonly IThumbnailGenerator _generator;
    private readonly ISourceAvailability _availability;

    public BuildThumbnailsHandler(
        IGalleryReader reader,
        IAssetRepository assets,
        IThumbnailStore store,
        IThumbnailGenerator generator,
        ISourceAvailability availability)
    {
        _reader = reader;
        _assets = assets;
        _store = store;
        _generator = generator;
        _availability = availability;
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

        var lost = new LostSources(_availability);

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
                            GeneratedThumbnail? generated = lost.IsLost(item.SourceRoot)
                                ? null
                                : await _generator
                                    .GenerateAsync(item.FullPath, item.Rotation, token)
                                    .ConfigureAwait(false);

                            if (generated is null)
                            {
                                // Recorded rather than simply counted, so this file
                                // is not opened again on every future pass - but
                                // only once its source has answered. Otherwise it
                                // is left for the next pass, uncounted.
                                if (lost.StillAnswers(item.SourceRoot))
                                {
                                    unreadable.Enqueue(item.AssetId);
                                    Interlocked.Increment(ref failed);
                                }
                            }
                            else if (await TrySaveAsync(generated, token)
                                         .ConfigureAwait(false) is not string name)
                            {
                                // Counted as a file that would not decode is, but
                                // not recorded: the original is fine and only its
                                // copy could not be put down, so the next pass
                                // tries it again.
                                Interlocked.Increment(ref failed);
                            }
                            else
                            {
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
    /// Stores one photograph's renditions, or answers null when something else
    /// was holding this one's file.
    /// </summary>
    /// <remarks>
    /// One picture that cannot be put down is that picture's problem, not the
    /// pass's. A save used to throw straight out of the pass, so a single file
    /// something else had open ended the whole scan, and with it every phase
    /// after this one and the folder counts the scan writes last.
    ///
    /// <para>But that is the only failure to save that is about one picture: a
    /// virus scanner, the search indexer or a backup holding its file at the
    /// moment it was put in place. Everything else the store raises is about the
    /// place every picture is kept - a full disk, write-protected media,
    /// permission refused on the working folder, the drive or the share it is
    /// on gone away - and refuses every picture alike. Answered here a picture
    /// at a time, the pass read every original in the library, an hour over the
    /// share, to record nothing and report the lot as pictures that could not be
    /// read, while the reason was thrown away and the next scan did it all
    /// again. So those still end the pass at the first picture, with the words
    /// the disk gave, as does any fault in the index, which is written elsewhere
    /// and is the screen's to report.</para>
    ///
    /// <para>Windows raises nearly all of them as a plain IOException, so the
    /// type cannot tell them apart and the error code does. The question asked
    /// is which failures are the picture's, never which are the library's: the
    /// ways a disk or a network can fail have no end, and one left off a list
    /// of them would cost that hour again.</para>
    /// </remarks>
    private async Task<string?> TrySaveAsync(
        GeneratedThumbnail generated, CancellationToken cancellationToken)
    {
        try
        {
            return await _store.SaveAsync(generated, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex) when (ex.HResult is SharingViolation or LockViolation)
        {
            return null;
        }
    }

    /// <summary>
    /// Photos with no tile on disk, whatever their row claims, on sources that
    /// can be reached.
    /// </summary>
    /// <remarks>
    /// Only the sources of photographs that need a tile are asked about, once
    /// each, so a library whose pictures are all made never waits on a share
    /// that is switched off.
    /// </remarks>
    private async Task<IReadOnlyList<PendingThumbnail>> FindPendingAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PendingThumbnail> candidates =
            await _reader.GetThumbnailCandidatesAsync(cancellationToken).ConfigureAwait(false);

        var reachable = new ReachableSources(_availability);

        return
        [
            .. candidates.Where(candidate =>
                !_store.Exists(candidate.ThumbnailName)
                && reachable.CanReach(candidate.SourceRoot)),
        ];
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
