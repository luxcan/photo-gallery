using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Updates;

/// <summary>
/// Whether a newer version of the app has been published.
/// </summary>
/// <remarks>
/// Asks, and never acts. The most this can do is hand back something for the
/// screen to offer; fetching a seventy megabyte executable, checking it,
/// replacing a running program and restarting it is a different piece of work
/// with a failure that leaves somebody with half an app. A sentence and a link
/// is the whole of what this is for.
///
/// <para>Silent about everything except good news, for the same reason the
/// shared-answer check is: the machine this runs on is often on a network that
/// cannot reach anything, and a complaint about that on every launch teaches
/// people to close the notice without reading it.</para>
/// </remarks>
public sealed class CheckForNewVersionHandler
{
    private readonly IReleaseSource _releases;

    public CheckForNewVersionHandler(IReleaseSource releases) => _releases = releases;

    /// <summary>
    /// The published version worth telling somebody about, or null.
    /// </summary>
    /// <remarks>
    /// Strictly newer, so a machine running the newest build says nothing, and a
    /// machine running something newer than was ever published - which is what a
    /// developer's own machine is most of the time - says nothing either.
    /// </remarks>
    public async Task<PublishedRelease?> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        // A build that cannot read its own version is in no position to judge
        // anybody else's. That is a broken build rather than an old one, and it
        // is not something a notice can help with.
        if (AppVersion.Comparable is not Version running)
        {
            return null;
        }

        PublishedRelease? newest =
            await _releases.NewestAsync(cancellationToken).ConfigureAwait(false);

        return newest is not null && newest.Number > running ? newest : null;
    }
}
