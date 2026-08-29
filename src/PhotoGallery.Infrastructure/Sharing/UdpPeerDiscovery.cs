using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing.Direct;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// Finding the other computers with a UDP multicast beacon.
/// </summary>
/// <remarks>
/// One self-contained file, and no dependency. <see cref="UdpClient"/> joins a
/// multicast group in four lines; an mDNS package would be a download, a licence
/// to read and a second thing to be wrong - for a house where the answer is
/// three laptops on one Wi-Fi.
///
/// <para>The group is <c>239.255.42.7</c>, in the administratively-scoped local
/// range, so a router will not forward it and the packet does not leave the
/// house.</para>
///
/// <para><strong>Every way this fails is silent.</strong> A network set to
/// Public blocks inbound traffic outright, a refused firewall prompt blocks the
/// socket, and guest Wi-Fi isolates machines from each other - and in all three
/// the packet simply goes nowhere and no error is raised. So the diagnosis is
/// made before the listening rather than inferred from an empty list.</para>
/// </remarks>
public sealed class UdpPeerDiscovery : IPeerDiscovery
{
    private static readonly IPAddress s_group = IPAddress.Parse(Beacon.Group);

    private readonly INetworkProfile _profile;

    public UdpPeerDiscovery(INetworkProfile profile) => _profile = profile;

    public Task<DiscoveryProblem> ReadinessAsync(CancellationToken cancellationToken = default)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return Task.FromResult(DiscoveryProblem.NoNetwork);
        }

        // Asked of Windows rather than guessed from a failure, because there is
        // no failure to guess from: a Public profile drops the inbound packet
        // and the socket is perfectly happy.
        if (_profile.IsPublic())
        {
            return Task.FromResult(DiscoveryProblem.PublicNetwork);
        }

        return Task.FromResult(DiscoveryProblem.None);
    }

    public async Task<IReadOnlyList<Beacon>> LookAsync(
        Beacon mine, TimeSpan listenFor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mine);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        Dictionary<Guid, Beacon> found = [];

        try
        {
            client.Client.SetSocketOption(
                SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, Beacon.GroupPort));
            client.JoinMulticastGroup(s_group);

            // Answered as well as sent. A machine that only called would need the
            // other person to have the screen open at the same moment, which is
            // exactly the two-person job this is arranged to avoid.
            byte[] packet = JsonSerializer.SerializeToUtf8Bytes(mine);
            await client
                .SendAsync(packet, new IPEndPoint(s_group, Beacon.GroupPort), cancellationToken)
                .ConfigureAwait(false);

            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(listenFor);

            while (!window.IsCancellationRequested)
            {
                UdpReceiveResult heard;

                try
                {
                    heard = await client.ReceiveAsync(window.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (Read(heard.Buffer) is Beacon them && them.IsWorthAnswering(mine.MachineId))
                {
                    // Answered directly, so a machine that was only listening
                    // learns about this one without having to call itself.
                    await client
                        .SendAsync(packet, heard.RemoteEndPoint, window.Token)
                        .ConfigureAwait(false);

                    found[them.MachineId] = them;
                }
            }
        }
        catch (SocketException)
        {
            // On Windows this is the firewall prompt having been refused. There
            // is nothing to retry and nothing to report from here: the caller
            // asked what could be found, and the answer is nothing.
            return [];
        }
        catch (OperationCanceledException)
        {
            // Stopped while listening. Whoever answered before that still counts.
        }

        return [.. found.Values];
    }

    /// <summary>
    /// A packet, or null where it was not one of ours.
    /// </summary>
    /// <remarks>
    /// Anything at all can arrive on a multicast group, including other
    /// software's packets and somebody's idea of a joke. Nothing here trusts the
    /// contents - it is a name and a port, and both are checked before use.
    /// </remarks>
    private static Beacon? Read(byte[] packet)
    {
        try
        {
            return JsonSerializer.Deserialize<Beacon>(packet);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
