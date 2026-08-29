namespace PhotoGallery.Application.Ports;

/// <summary>
/// Whether Windows thinks this network is a public one.
/// </summary>
/// <remarks>
/// Its own seam for one question, because that question cannot be answered by
/// trying. A Public profile blocks inbound traffic outright: the socket opens,
/// the packet is sent, nothing comes back, and no error is raised anywhere. The
/// only way to tell that apart from "nobody else is running the app" is to ask
/// Windows directly - and the only way to test the answer is to be able to
/// stand in for it.
/// </remarks>
public interface INetworkProfile
{
    /// <summary>
    /// True when at least one connected network is set to Public.
    /// </summary>
    /// <remarks>
    /// Any, not all. A laptop on a private home network and a public hotspot at
    /// once will have the hotspot dropping the packets, and the person looking
    /// at an empty list deserves to be told why.
    /// </remarks>
    bool IsPublic();
}
