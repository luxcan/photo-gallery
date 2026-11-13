using System.Collections.Concurrent;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Sources;

/// <summary>
/// The sources a long pass has found gone part way through.
/// </summary>
/// <remarks>
/// <see cref="ReachableSources"/> answers once at the start of an operation;
/// this answers for the rest of it. A pass reading thousands of originals can
/// outlast the share it started on, and then every file left looks broken - the
/// same "could not open" a corrupt file gives. So when a file will not open, the
/// pass asks its source again before believing the file. A source found gone is
/// remembered, so a dropped share costs one wait and the rest of its files are
/// passed over rather than each tried.
///
/// <para>Answers that a source is there are not remembered: the case this exists
/// for is a source that was there a moment ago. Safe to ask from several
/// threads at once, because the passes that use it read in parallel.</para>
/// </remarks>
public sealed class LostSources
{
    private readonly ISourceAvailability _availability;

    private readonly ConcurrentDictionary<string, bool> _lost =
        new(StringComparer.OrdinalIgnoreCase);

    public LostSources(ISourceAvailability availability) =>
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));

    /// <summary>Whether this source has already been found gone in this pass.</summary>
    public bool IsLost(string sourceRoot) => _lost.ContainsKey(sourceRoot);

    /// <summary>
    /// Whether this source still answers, asked afresh unless it is already
    /// known to be gone.
    /// </summary>
    public bool StillAnswers(string sourceRoot)
    {
        if (IsLost(sourceRoot))
        {
            return false;
        }

        if (_availability.CanReach(sourceRoot))
        {
            return true;
        }

        _lost.TryAdd(sourceRoot, true);
        return false;
    }
}
