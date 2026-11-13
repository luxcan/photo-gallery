using System.Collections.Concurrent;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Sources;

/// <summary>
/// Asks whether each source can be reached, once, for the length of one
/// operation.
/// </summary>
/// <remarks>
/// Remembered rather than asked again because the answer is a network round
/// trip, and on an absent share it is a slow one: measured at <b>21 seconds</b>
/// to fail against this library's NAS on a cold call. Per file, deleting four
/// hundred duplicates would sit there for two hours before refusing any of them.
/// Per source, it costs one wait and then nothing.
///
/// <para>Deliberately short-lived - one of these per operation, not one per
/// application. A share that comes back is noticed by the next thing the user
/// does, and a share that drops half way through a batch is caught by the rows
/// that follow. This remembers what an operation has already found out; it is
/// not a cache of the world.</para>
///
/// <para>Safe to ask from several threads at once, because the passes that read
/// originals read them in parallel.</para>
/// </remarks>
public sealed class ReachableSources
{
    private readonly ISourceAvailability _availability;

    private readonly ConcurrentDictionary<string, bool> _answered =
        new(StringComparer.OrdinalIgnoreCase);

    public ReachableSources(ISourceAvailability availability) => _availability = availability;

    /// <inheritdoc cref="ISourceAvailability.CanReach"/>
    public bool CanReach(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        return _answered.GetOrAdd(sourceRoot, _availability.CanReach);
    }

    /// <summary>Whether this source has been found gone during this operation.</summary>
    public bool IsLost(string sourceRoot) =>
        _answered.TryGetValue(sourceRoot, out bool reachable) && !reachable;

    /// <summary>
    /// Whether this source still answers, asked afresh unless it has already
    /// been found gone.
    /// </summary>
    /// <remarks>
    /// For a long pass to ask when one of its files will not open. The pass can
    /// outlast the share it started on, and then every file left looks broken -
    /// the same "could not open" a corrupt file gives - so the source is asked
    /// before the file is believed. That it was there at the start is exactly
    /// the answer that may have gone stale, so it is not trusted; that it has
    /// gone is remembered, so a dropped share costs one wait and the rest of its
    /// files are passed over rather than each tried.
    /// </remarks>
    public bool StillAnswers(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || IsLost(sourceRoot))
        {
            return false;
        }

        if (_availability.CanReach(sourceRoot))
        {
            // Never written over "gone": another thread may have found it gone
            // after this one asked.
            _answered.TryAdd(sourceRoot, true);
            return true;
        }

        _answered[sourceRoot] = false;
        return false;
    }

    /// <summary>
    /// Which of these roots cannot be reached, named once each and in order.
    /// </summary>
    /// <remarks>
    /// For putting to the user before anything is attempted, so a refusal names
    /// the share rather than the four hundred files on it.
    /// </remarks>
    public IReadOnlyList<string> OutOfReach(IEnumerable<string> sourceRoots)
    {
        ArgumentNullException.ThrowIfNull(sourceRoots);

        return
        [
            .. sourceRoots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(root => !CanReach(root))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }
}
