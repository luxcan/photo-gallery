using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// How this library's answers reach the other machines in the house, and
/// theirs reach it.
/// </summary>
/// <remarks>
/// A shared folder is one file written and everybody else's read. It is the
/// only way answers travel, and the seam is kept anyway because
/// <strong>the merge must never learn how they arrived.</strong> Everything
/// downstream is defined on decision sets, not on where they came from.
///
/// <para>Separate from anything that moves renditions, because the two have
/// different shapes and different costs: decisions are one small document
/// written whole, renditions are tens of thousands of files copied one at a time
/// and stopped halfway more often than not. Keeping them apart is also what lets
/// a machine take the decisions and decline the gigabytes.</para>
/// </remarks>
public interface IDecisionExchange
{
    /// <summary>
    /// Whether there is anywhere to exchange answers yet, and why not when there
    /// is not.
    /// </summary>
    /// <remarks>
    /// Asked rather than discovered by a failure, because the Sharing screen has
    /// to open by saying what will happen before anything is nominated.
    /// </remarks>
    Task<ExchangeReadiness> ReadinessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes this machine's whole decision set, replacing whatever it wrote
    /// last time.
    /// </summary>
    Task PublishAsync(DecisionSet mine, CancellationToken cancellationToken = default);

    /// <summary>
    /// When each machine last put its answers where this one could see them.
    /// </summary>
    /// <remarks>
    /// The honest form of "is everybody in step?" for a mechanism whose whole
    /// advantage is that it does not need two laptops on at once - which also
    /// means presence, in the usual sense, is not a thing it can report.
    /// Recency is. Without it a decision set written six months ago by a laptop
    /// now in a drawer merges exactly like one written an hour ago, and there is
    /// nothing on screen to tell them apart.
    ///
    /// <para>Deliberately cheap: this is a directory listing, not a read. The
    /// Sharing screen asks it every time it opens, and decompressing half a
    /// megabyte per machine to draw a line of text would be a screen nobody
    /// opens twice.</para>
    /// </remarks>
    Task<IReadOnlyList<PublishedAnswers>> StandingAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads what every other machine has published.</summary>
    Task<FetchedDecisions> FetchAsync(CancellationToken cancellationToken = default);
}
