using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// Talking to another computer directly, rather than through a folder.
/// </summary>
/// <remarks>
/// A seam over the sockets, so that reaching a machine and pairing with it can
/// be argued out without two real laptops - and so that the use cases above it
/// say nothing about TLS. What comes back over it is the same decision set a
/// shared folder holds, read by the same code: the merge cannot tell the two
/// apart and must never learn to.
/// </remarks>
public interface IPeerLink
{
    /// <summary>
    /// Asks who is at an address. The one thing a machine answers unpaired.
    /// </summary>
    Task<PeerFound> GreetAsync(
        string host, int port, MachineIdentity me, CancellationToken cancellationToken = default);

    /// <summary>Offers six digits, and answers whether the other end agreed.</summary>
    Task<PeerPairing> PairAsync(
        string host,
        int port,
        MachineIdentity me,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Asks a paired machine for everything it has decided.</summary>
    /// <remarks>
    /// The fingerprint is what is trusted, not the address: a machine can call
    /// itself anything and cannot call itself by somebody else's key.
    /// </remarks>
    Task<DecisionSet?> AskAsync(
        string host,
        int port,
        MachineIdentity me,
        string fingerprint,
        CancellationToken cancellationToken = default);
}

/// <summary>Who answered at an address, and what their key says they are.</summary>
public sealed record PeerFound(
    Guid MachineId, string Name, string AppVersion, int SchemaVersion, string Fingerprint);

/// <summary>Whether six digits agreed, and who with.</summary>
public sealed record PeerPairing(
    bool Succeeded, Guid MachineId, string Name, string Fingerprint, string Problem);
