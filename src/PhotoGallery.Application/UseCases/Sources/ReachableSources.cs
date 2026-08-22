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
/// </remarks>
public sealed class ReachableSources
{
    private readonly ISourceAvailability _availability;

    private readonly Dictionary<string, bool> _answered =
        new(StringComparer.OrdinalIgnoreCase);

    public ReachableSources(ISourceAvailability availability) => _availability = availability;

    /// <inheritdoc cref="ISourceAvailability.CanReach"/>
    public bool CanReach(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
        {
            return false;
        }

        if (_answered.TryGetValue(sourceRoot, out bool known))
        {
            return known;
        }

        bool reachable = _availability.CanReach(sourceRoot);
        _answered[sourceRoot] = reachable;
        return reachable;
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
