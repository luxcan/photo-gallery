using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Faces;

/// <summary>
/// Finds the faces in every picture that has not been looked at yet.
/// </summary>
/// <remarks>
/// Reads the cached previews and nothing else. The originals were read once when
/// the previews were made and are never opened again, so this pass touches no
/// network and costs no bandwidth however far away the pictures live.
///
/// <para>Shaped like the preparing pass because it has the same problem: it runs
/// for a long time, it can be stopped, and what it has already finished must
/// survive that. Work is batched, each batch is read by several threads at once
/// and written by one, and the write happens in a <c>finally</c> so an
/// interrupted batch still records the pictures it got through.</para>
/// </remarks>
public sealed class DetectFacesHandler
{
    /// <summary>
    /// How many previews are examined before their faces are written.
    /// </summary>
    /// <remarks>
    /// Small on purpose, as it is in the preparing pass: it buys no efficiency,
    /// and all it decides is how much an interrupted pass has to do again.
    /// </remarks>
    private const int SaveBatchSize = 20;

    /// <summary>
    /// How many previews are examined at once when the caller has no opinion.
    /// </summary>
    /// <remarks>
    /// Measured over 300 real previews on a 22-core machine: one at a time is
    /// 595 ms each, four is 188, eight is 144 and twelve is 106. The work scales
    /// with cores rather than with waiting, which is the opposite of the
    /// preparing pass and its fixed eight - that one is dominated by a network
    /// round trip. Half the cores leaves the machine usable while a twenty
    /// minute pass runs, and the cap keeps a very large machine from spending
    /// more on contention than it gains.
    /// </remarks>
    public static int DefaultParallelism => Math.Clamp(Environment.ProcessorCount / 2, 2, 12);

    private readonly IGalleryReader _reader;
    private readonly IThumbnailStore _store;
    private readonly IFaceScanner _scanner;
    private readonly IFaceRepository _faces;
    private readonly IModelStore _models;

    public DetectFacesHandler(
        IGalleryReader reader,
        IThumbnailStore store,
        IFaceScanner scanner,
        IFaceRepository faces,
        IModelStore models)
    {
        _reader = reader;
        _store = store;
        _scanner = scanner;
        _faces = faces;
        _models = models;
    }

    /// <param name="degreeOfParallelism">
    /// How many previews are examined at once, or zero to let
    /// <see cref="DefaultParallelism"/> decide. Each graph is deliberately held
    /// to one thread of its own, so this number is the whole of the parallelism.
    /// </param>
    public async Task<FaceDetectionResult> HandleAsync(
        int degreeOfParallelism = 0,
        IProgress<FaceDetectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // What there is to do, before whether it can be done. The order matters
        // now that this runs at the end of every scan rather than only when
        // somebody pressed a button: asking whether the weights are installed
        // means checksumming 1.7 GB of them, and a rescan that found nothing new
        // - which is most rescans - must not pay that to discover it had nothing
        // to look at. The describing pass has always been this way round.
        IReadOnlyList<PendingFaceScan> pending;
        try
        {
            pending = await FindPendingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped during the opening query, which takes the token and is the
            // one thing here that raises. Answered rather than thrown, because
            // every other way out of this method is an answer - and since this
            // became the last phase of a scan, the exception had nowhere to go
            // but the dispatcher.
            return new FaceDetectionResult(
                0, 0, 0, 0, stopwatch.Elapsed, WasCancelled: true, ModelsMissing: false);
        }

        if (pending.Count == 0)
        {
            return new FaceDetectionResult(0, 0, 0, 0, stopwatch.Elapsed, false, false);
        }

        if (_models.StateOf(ModelId.FaceDetection) != ModelState.Ready
            || _models.StateOf(ModelId.FaceRecognition) != ModelState.Ready)
        {
            // Checked once, before anything is read: the alternative is
            // discovering it eleven thousand times over.
            return FaceDetectionResult.WithoutModels(stopwatch.Elapsed);
        }

        int scanned = 0, facesFound = 0, failed = 0, done = 0;
        bool cancelled = false;

        try
        {
            foreach (PendingFaceScan[] batch in pending.Chunk(SaveBatchSize))
            {
                var completed = new ConcurrentQueue<FaceScanUpdate>();

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
                            IReadOnlyList<ScannedFace>? found = await _scanner
                                .ScanAsync(item.PreviewPath, token)
                                .ConfigureAwait(false);

                            if (found is null)
                            {
                                // Left unmarked rather than recorded as failed.
                                // A preview is this app's own file and the
                                // preparing pass can make it again, so the next
                                // run should try once more instead of writing
                                // the picture off for good.
                                Interlocked.Increment(ref failed);
                            }
                            else
                            {
                                completed.Enqueue(new FaceScanUpdate(
                                    item.AssetIds, found, DateTime.UtcNow));

                                Interlocked.Add(ref facesFound, found.Count);
                                Interlocked.Increment(ref scanned);
                            }

                            int seen = Interlocked.Increment(ref done);
                            if (seen % 25 == 0)
                            {
                                progress?.Report(new FaceDetectionProgress(
                                    seen, pending.Count, facesFound, failed));
                            }
                        }).ConfigureAwait(false);
                }
                finally
                {
                    // In a finally so a batch stopped part way keeps what it
                    // finished. Written from this one thread rather than from
                    // the workers: SQLite tolerates concurrent readers, not
                    // concurrent writers.
                    await SaveAsync(completed).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        progress?.Report(new FaceDetectionProgress(done, pending.Count, facesFound, failed));

        return new FaceDetectionResult(
            pending.Count, scanned, facesFound, failed, stopwatch.Elapsed, cancelled, false);
    }

    /// <summary>
    /// The previews still to read, one entry per distinct rendition.
    /// </summary>
    private async Task<IReadOnlyList<PendingFaceScan>> FindPendingAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FaceScanCandidate> candidates =
            await _reader.GetFaceCandidatesAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. candidates
                .Where(NeedsLooking)
                .GroupBy(candidate => candidate.ThumbnailName, StringComparer.OrdinalIgnoreCase)
                .Select(shared => new PendingFaceScan(
                    _store.ResolvePreviewPath(shared.Key),
                    [.. shared.Select(candidate => candidate.AssetId)])),
        ];
    }

    /// <summary>
    /// Whether this photograph's faces are missing or out of date.
    /// </summary>
    /// <remarks>
    /// Never looked at is the obvious case. The other is a preview that has been
    /// rewritten since it was: a rendition is named after the original's content
    /// and can be rebuilt under the same name, so the row's own marker cannot
    /// tell that the image changed and the faces recorded against the old one
    /// would stand forever. Measured on this library, twenty-five photographs
    /// were in exactly that state - one of them missing a face that is plainly
    /// there, because it was found in a preview that no longer exists.
    ///
    /// <para>One extra timestamp read per photograph, replacing the existence
    /// check that was here - the same single stat, asked a better question.</para>
    /// </remarks>
    private bool NeedsLooking(FaceScanCandidate candidate)
    {
        if (_store.PreviewWrittenUtc(candidate.ThumbnailName) is not DateTime written)
        {
            // No preview to read. The preparing pass makes it, and this looks
            // again next time.
            return false;
        }

        // A second's grace: the write and the record of it happen moments apart
        // during a pass, and a clock that disagrees with itself by a tick should
        // not put every photograph back in the queue for ever.
        return candidate.DetectedUtc is not DateTime detected
            || written > detected.AddSeconds(1);
    }

    private async Task SaveAsync(ConcurrentQueue<FaceScanUpdate> completed)
    {
        var batch = new List<FaceScanUpdate>(completed.Count);
        while (completed.TryDequeue(out FaceScanUpdate? update))
        {
            batch.Add(update);
        }

        if (batch.Count > 0)
        {
            // Not cancellable: this work has been done, and a row that did not
            // record it would have the next pass do it again.
            await _faces.SaveAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
