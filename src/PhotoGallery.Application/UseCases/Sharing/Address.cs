using PhotoGallery.Domain.Sharing.Direct;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// A computer's address as somebody types it.
/// </summary>
/// <remarks>
/// A name or an address, with a port only if they know one - and they will not.
/// The port is what the beacon carries, and the whole reason a typed address
/// exists is that the beacon did not arrive, so it has to be assumed.
/// </remarks>
public static class Address
{
    public static bool TryParse(string? typed, out string host, out int port)
    {
        host = string.Empty;
        port = PeerPorts.Preferred;

        if (string.IsNullOrWhiteSpace(typed))
        {
            return false;
        }

        string text = typed.Trim();

        // A bracketed IPv6 literal, which is the one form where a colon is not a
        // port separator.
        if (text.StartsWith('['))
        {
            int close = text.IndexOf(']', StringComparison.Ordinal);

            if (close < 0)
            {
                return false;
            }

            host = text[1..close];
            string rest = text[(close + 1)..];

            return rest.Length == 0
                || (rest.StartsWith(':') && int.TryParse(rest[1..], out port) && IsPort(port));
        }

        int colon = text.LastIndexOf(':');

        if (colon > 0 && int.TryParse(text[(colon + 1)..], out int typedPort) && IsPort(typedPort))
        {
            host = text[..colon];
            port = typedPort;
            return host.Length > 0;
        }

        host = text;
        return host.Length > 0 && !host.Contains(' ', StringComparison.Ordinal);
    }

    private static bool IsPort(int port) => port is > 0 and <= 65535;
}

/// <summary>Where a machine listens, when nothing has said otherwise.</summary>
public static class PeerPorts
{
    /// <summary>
    /// One above the beacon's, and in the same unassigned range.
    /// </summary>
    /// <remarks>
    /// It has to be guessable: every case the discovery diagnosis names ends
    /// with "type the address instead", and an address is no use without a port
    /// that nobody is going to read off a screen.
    /// </remarks>
    public const int Preferred = Beacon.GroupPort + 1;
}
