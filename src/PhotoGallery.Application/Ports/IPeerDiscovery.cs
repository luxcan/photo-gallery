using PhotoGallery.Domain.Sharing.Direct;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// How the other computers in the house are found without a folder in common.
/// </summary>
/// <remarks>
/// The second way to reach another machine, and the one that ships second. The
/// shared folder wins on the deciding fact - it does not need the other laptop
/// switched on - but it cannot help a machine that shares no folder with
/// anybody, and that is what this is for.
///
/// <para><strong>Listen always, announce only while the Sharing screen is
/// open.</strong> The other way round makes it a two-person job, both screens
/// open at once; and an app that announces itself on the family network for ever
/// is not something to put on somebody else's laptop. So the person who wants to
/// share opens the screen, their machine calls, and the quiet ones answer.</para>
/// </remarks>
public interface IPeerDiscovery
{
    /// <summary>
    /// Whether the other computers can be found at all, and why not when they
    /// cannot.
    /// </summary>
    /// <remarks>
    /// Asked before anything is listed, because an empty list looks the same
    /// whether nobody is running the app or the network will never carry the
    /// packet - and the two need completely different things from the person
    /// reading it.
    /// </remarks>
    Task<DiscoveryProblem> ReadinessAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls out on the family network and answers with whoever replies.
    /// </summary>
    /// <param name="mine">What to say about this machine.</param>
    /// <param name="listenFor">
    /// How long to wait for answers. Short: this runs while somebody is looking
    /// at a screen, and a machine that has not answered in a few seconds is one
    /// they will try again for.
    /// </param>
    Task<IReadOnlyList<Beacon>> LookAsync(
        Beacon mine, TimeSpan listenFor, CancellationToken cancellationToken = default);
}
