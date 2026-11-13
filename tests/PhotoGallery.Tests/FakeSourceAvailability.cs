using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests;

/// <summary>
/// Photo sources that are there unless a test says one is away, and a record of
/// every root that was asked about.
/// </summary>
/// <remarks>
/// Safe to ask from several threads at once, because the passes that ask it
/// read their originals in parallel.
/// </remarks>
internal sealed class FakeSourceAvailability : ISourceAvailability
{
    private readonly object _gate = new();
    private readonly List<string> _asked = [];

    /// <summary>Roots that cannot be reached.</summary>
    public HashSet<string> Away { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every root asked about, in the order asked, repeats included.</summary>
    public IReadOnlyList<string> Asked
    {
        get
        {
            lock (_gate)
            {
                return [.. _asked];
            }
        }
    }

    /// <summary>
    /// Takes a source away while a pass is running, from whichever of its
    /// threads gets there first.
    /// </summary>
    public void SendAway(string sourceRoot)
    {
        lock (_gate)
        {
            Away.Add(sourceRoot);
        }
    }

    public bool CanReach(string sourceRoot)
    {
        lock (_gate)
        {
            _asked.Add(sourceRoot);
            return !Away.Contains(sourceRoot);
        }
    }
}
