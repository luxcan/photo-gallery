namespace PhotoGallery.Application.Ports;

/// <summary>Where new versions of the app are announced.</summary>
/// <remarks>
/// A port because reaching it is a network call and the rule about what counts
/// as newer is not. The rule is worth testing and the network is not worth
/// testing, so the two are on opposite sides of this line.
/// </remarks>
public interface IReleaseSource
{
    /// <summary>
    /// The newest version published, or null when there is nothing to report.
    /// </summary>
    /// <remarks>
    /// <strong>Null covers every kind of nothing, including failure.</strong> No
    /// network, no answer, an answer that cannot be understood, nothing
    /// published yet - a person opening a photo album can act on none of those,
    /// and an app that complains about the internet every time it starts is an
    /// app people learn to ignore. What cannot be reached is simply not news.
    /// </remarks>
    Task<PublishedRelease?> NewestAsync(CancellationToken cancellationToken = default);
}

/// <summary>A version somebody has published, as the app needs to know it.</summary>
/// <param name="Number">
/// The version itself, already made sense of. A release whose name is not a
/// version - a tag somebody typed by hand, or one marked as a pre-release - is
/// not carried here at all, because there is nothing to compare it with.
/// </param>
/// <param name="Name">What it is called, for the sentence on screen.</param>
/// <param name="Page">Where a person goes to read about it and fetch it.</param>
public sealed record PublishedRelease(Version Number, string Name, string Page);
