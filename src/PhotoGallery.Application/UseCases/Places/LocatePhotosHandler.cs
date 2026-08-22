using System.Collections.Concurrent;
using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sources;

namespace PhotoGallery.Application.UseCases.Places;

/// <summary>
/// Works out where each photograph was taken, and what that place is called.
/// </summary>
/// <remarks>
/// Two jobs in one pass because they share an outstanding set and a marker.
/// Reading the coordinates is the expensive half - an original opened over the
/// share - and naming them is arithmetic against a gazetteer already in memory,
/// so splitting them would mean two passes over the same rows, two markers to
/// keep in step, and a photograph that could sit with coordinates and no name
/// because only one of the two had been run.
///
/// <para>Most photographs here need no file opened at all. Any prepared since
/// the app learnt to read GPS already carries its coordinates, and for those
/// this is a lookup and a write - which is also why an absent share does not
/// stop the pass: it stops only the part of it that needs the share.</para>
///
/// <para>Shaped like the describing and preparing passes, because it has their
/// problem: it runs for tens of minutes on a full library, it can be stopped,
/// and what it finished must survive that.</para>
/// </remarks>
public sealed class LocatePhotosHandler
{
    /// <summary>How many photographs are settled before their answers are written.</summary>
    private const int SaveBatchSize = 20;

    /// <summary>
    /// How many photographs pass between progress reports.
    /// </summary>
    /// <remarks>
    /// Twenty-five, matching the preparing pass, because a file here costs about
    /// what a file costs there - three quarters of a second over the share.
    /// </remarks>
    private const int ReportEvery = 25;

    /// <summary>
    /// How many originals are opened at once when the caller has no opinion.
    /// </summary>
    /// <remarks>
    /// Eight, as the preparing pass uses, and for its reason rather than the
    /// describing pass's: this is latency-bound on a network share, not
    /// CPU-bound, so the right number follows the link and not the processor.
    /// </remarks>
    private const int DefaultParallelism = 8;

    private readonly IGalleryReader _reader;
    private readonly IOriginalCoordinates _coordinates;
    private readonly IGeocoder _geocoder;
    private readonly IPlaceRepository _places;
    private readonly IAssetRepository _assets;
    private readonly ISourceAvailability _availability;

    public LocatePhotosHandler(
        IGalleryReader reader,
        IOriginalCoordinates coordinates,
        IGeocoder geocoder,
        IPlaceRepository places,
        IAssetRepository assets,
        ISourceAvailability availability)
    {
        _reader = reader;
        _coordinates = coordinates;
        _geocoder = geocoder;
        _places = places;
        _assets = assets;
        _availability = availability;
    }

    public async Task<LocatePhotosResult> HandleAsync(
        int degreeOfParallelism = 0,
        IProgress<LocatePhotosProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<LocationCandidate> outstanding;
        try
        {
            outstanding = await _reader.GetLocationCandidatesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped during the opening query, which takes the token. Answered
            // rather than thrown, as every other way out of this method is.
            return new LocatePhotosResult(0, 0, 0, 0, [], stopwatch.Elapsed, Cancelled: true);
        }

        if (outstanding.Count == 0)
        {
            return LocatePhotosResult.Nothing(stopwatch.Elapsed);
        }

        // One of these for this run, as its own documentation requires. An absent
        // share costs one twenty-one second wait rather than one per photograph.
        var reachable = new ReachableSources(_availability);

        // Only the photographs that still need their file opened care whether the
        // share is there. Asking about the others' roots would refuse work that
        // needs no file, and would make a laptop away from the NAS unable to
        // finish naming pictures whose coordinates are already in the index.
        IReadOnlyList<string> unreachable = reachable.OutOfReach(
            outstanding.Where(c => !c.IsAlreadyRead).Select(c => c.SourceRoot));

        LocationCandidate[] pending =
        [
            .. outstanding.Where(candidate =>
                candidate.IsAlreadyRead || reachable.CanReach(candidate.SourceRoot)),
        ];

        if (pending.Length == 0)
        {
            return new LocatePhotosResult(0, 0, 0, 0, unreachable, stopwatch.Elapsed, false);
        }

        int located = 0, named = 0, unreadable = 0, done = 0;
        bool cancelled = false;

        // Reported before any work, so the screen names this phase as it begins
        // rather than as it ends.
        progress?.Report(new LocatePhotosProgress(0, pending.Length, 0, stopwatch.Elapsed));

        try
        {
            foreach (LocationCandidate[] batch in pending.Chunk(SaveBatchSize))
            {
                var settled = new ConcurrentQueue<SettledLocation>();

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
                        (item, token) =>
                        {
                            SettledLocation? answer = Examine(item);

                            if (answer is not SettledLocation settledItem)
                            {
                                Interlocked.Increment(ref unreadable);
                            }
                            else
                            {
                                settled.Enqueue(settledItem);

                                if (settledItem.Latitude is not null)
                                {
                                    Interlocked.Increment(ref located);
                                }

                                if (settledItem.Place is not null)
                                {
                                    Interlocked.Increment(ref named);
                                }
                            }

                            int seen = Interlocked.Increment(ref done);
                            if (seen % ReportEvery == 0)
                            {
                                progress?.Report(new LocatePhotosProgress(
                                    seen, pending.Length, Volatile.Read(ref named),
                                    stopwatch.Elapsed));
                            }

                            return ValueTask.CompletedTask;
                        }).ConfigureAwait(false);
                }
                finally
                {
                    // In a finally so a batch stopped part way keeps what it
                    // settled. Written from this one thread rather than the
                    // workers: SQLite tolerates concurrent readers, not
                    // concurrent writers.
                    await SaveAsync(settled).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        stopwatch.Stop();
        progress?.Report(
            new LocatePhotosProgress(done, pending.Length, named, stopwatch.Elapsed));

        return new LocatePhotosResult(
            done, located, named, unreadable, unreachable, stopwatch.Elapsed, cancelled);
    }

    /// <summary>
    /// Settles one photograph: its coordinates, then the name of the nearest
    /// place to them.
    /// </summary>
    /// <returns>
    /// The answer, or null when the file could not be read - which is not an
    /// answer and must not be written down. This is the case the three-outcome
    /// reading exists for: recording it as "no coordinates" would leave the
    /// photograph unplaced for good the moment the share came back.
    /// </returns>
    private SettledLocation? Examine(LocationCandidate item)
    {
        double? latitude = item.Latitude;
        double? longitude = item.Longitude;

        if (!item.IsAlreadyRead)
        {
            CoordinateReading reading = _coordinates.Read(item.FullPath);
            if (!reading.IsSettled)
            {
                return null;
            }

            bool found = reading.Outcome == CoordinateOutcome.Found;
            latitude = found ? reading.Latitude : null;
            longitude = found ? reading.Longitude : null;
        }

        GazetteerPlace? place = latitude is double north && longitude is double east
            ? _geocoder.Resolve(north, east)
            : null;

        return new SettledLocation(item.AssetId, latitude, longitude, place);
    }

    /// <summary>
    /// Writes one batch: the places first, so each photograph has a row to point
    /// at.
    /// </summary>
    private async Task SaveAsync(ConcurrentQueue<SettledLocation> settled)
    {
        var batch = new List<SettledLocation>(settled.Count);
        while (settled.TryDequeue(out SettledLocation item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
        {
            return;
        }

        // Not cancellable, in either call: this work has been done, and a row
        // that did not record it would have the next run open the same files.
        IReadOnlyDictionary<int, int> byGeoNameId = await _places
            .EnsureAsync(
                [.. batch.Select(item => item.Place).OfType<GazetteerPlace>()],
                CancellationToken.None)
            .ConfigureAwait(false);

        await _assets
            .RecordLocationsAsync(
                [
                    .. batch.Select(item => new PhotoLocation(
                        item.AssetId,
                        item.Latitude,
                        item.Longitude,
                        item.Place is GazetteerPlace found
                            ? byGeoNameId[found.GeoNameId]
                            : null,
                        DateTime.UtcNow)),
                ],
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>One photograph's answer, before the place has a row of its own.</summary>
    private readonly record struct SettledLocation(
        int AssetId, double? Latitude, double? Longitude, GazetteerPlace? Place);
}
