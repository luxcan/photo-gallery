using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Sharing.Direct;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// The end of a direct connection that waits to be spoken to.
/// </summary>
/// <remarks>
/// <strong>Listening is always on; announcing is not.</strong> A machine that
/// only listened would need the other person to have their screen open at the
/// same moment, which is exactly the two-person job this is arranged to avoid -
/// and a machine that announced itself for ever is not something to put on
/// somebody else's laptop. So the person who wants to share opens the screen,
/// their machine calls, and the quiet ones answer.
///
/// <para>What it will say without being paired is who it is. Everything else -
/// which is to say every decision anybody in the house has made - needs a
/// fingerprint this library has already agreed to.</para>
/// </remarks>
public sealed class PeerListener : IAsyncDisposable
{
    private readonly PeerCertificate _certificate;
    private readonly Func<Task<DecisionSet>> _mine;
    private readonly Func<Guid> _machine;
    private readonly Func<string, bool> _isPaired;

    private TcpListener? _listener;
    private CancellationTokenSource? _stopping;
    private Task? _serving;

    /// <param name="mine">What this library has decided, read fresh per request.</param>
    /// <param name="isPaired">
    /// Whether a fingerprint is one this library has agreed to. Asked per
    /// request rather than captured once, because pairing happens while this is
    /// running.
    /// </param>
    public PeerListener(
        PeerCertificate certificate,
        Func<Task<DecisionSet>> mine,
        Func<Guid> machine,
        Func<string, bool> isPaired)
    {
        _certificate = certificate;
        _mine = mine;
        _machine = machine;
        _isPaired = isPaired;
    }

    /// <summary>The port being listened on, or 0 when nothing is.</summary>
    public int Port { get; private set; }

    /// <summary>
    /// A six-digit code somebody has been shown, or null when nobody is
    /// pairing.
    /// </summary>
    /// <remarks>
    /// Set while the Sharing screen is offering to pair and cleared afterwards.
    /// A machine that would accept a pairing at any time is one anybody on the
    /// Wi-Fi can guess their way into at their leisure.
    /// </remarks>
    public string? Offering { get; set; }

    /// <summary>Raised when a pairing succeeds, so it can be remembered.</summary>
    public event EventHandler<PeerPairing>? Paired;

    /// <summary>
    /// Starts listening, and answers the port.
    /// </summary>
    /// <remarks>
    /// The preferred port where it is free, and any free port otherwise. Both
    /// are announced in the beacon, so discovery works either way; falling back
    /// only costs the typed address, and refusing to start would cost
    /// everything.
    /// </remarks>
    public int Start()
    {
        if (_listener is not null)
        {
            return Port;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Any, PeerPorts.Preferred);
            _listener.Start();
        }
        catch (SocketException)
        {
            // Something else has it, which on a machine running two copies of
            // this app is exactly what happens.
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start();
        }

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _stopping = new CancellationTokenSource();
        _serving = ServeAsync(_stopping.Token);

        return Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stopping is not null)
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
        }

        _listener?.Stop();

        if (_serving is not null)
        {
            try
            {
                await _serving.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Asked to stop, and it did.
            }
        }

        _stopping?.Dispose();
        _listener = null;
        _serving = null;
        Port = 0;
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException
                                          or ObjectDisposedException)
            {
                return;
            }

            // Not awaited. One machine taking its time must not stop the others
            // being answered, and every failure inside is already contained.
            _ = HandleAsync(client, cancellationToken);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                await using var stream = new SslStream(client.GetStream(), false);

                await stream.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate.Mine(),

                        // Asked for, and accepted whatever it is. The fingerprint
                        // is what this machine actually checks, and refusing an
                        // unsigned certificate here would refuse every peer.
                        ClientCertificateRequired = true,
                        RemoteCertificateValidationCallback = (_, _, _, _) => true,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    },
                    cancellationToken).ConfigureAwait(false);

                PeerMessage? ask = await PeerFraming
                    .ReadMessageAsync(stream, cancellationToken)
                    .ConfigureAwait(false);

                if (ask is null)
                {
                    return;
                }

                await AnswerAsync(stream, ask, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException
                                      or AuthenticationException
                                      or SocketException
                                      or InvalidDataException
                                      or OperationCanceledException
                                      or ObjectDisposedException)
        {
            // One conversation going wrong is not this machine's problem to
            // report: nobody here asked for it, and the other end already knows.
        }
    }

    private async Task AnswerAsync(
        SslStream stream, PeerMessage ask, CancellationToken cancellationToken)
    {
        string theirs = stream.RemoteCertificate is X509Certificate certificate
            ? PeerCertificate.FingerprintOf(
                X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()))
            : string.Empty;

        PeerMessage me = new(
            PeerAsk.Hello,
            _machine(),
            Environment.MachineName,
            typeof(PeerListener).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            SharingVersion.Schema,
            string.Empty,
            Refused: false);

        switch (ask.Ask)
        {
            case PeerAsk.Hello:
                await PeerFraming.WriteAsync(stream, me, cancellationToken).ConfigureAwait(false);
                break;

            case PeerAsk.Pair:
                await PairAsync(stream, ask, me, theirs, cancellationToken).ConfigureAwait(false);
                break;

            case PeerAsk.Decisions:
                await DecisionsAsync(stream, me, theirs, cancellationToken).ConfigureAwait(false);
                break;

            default:
                await PeerFraming
                    .WriteAsync(stream, me with { Refused = true }, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private async Task PairAsync(
        SslStream stream,
        PeerMessage ask,
        PeerMessage me,
        string theirs,
        CancellationToken cancellationToken)
    {
        // Only while somebody is looking at the screen that offered a code. A
        // machine that would pair at any time is one anybody on the Wi-Fi can
        // guess their way into at their leisure - a million tries, unattended.
        if (Offering is not string code || theirs.Length == 0)
        {
            await PeerFraming
                .WriteAsync(stream, me with { Refused = true }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        string check = PairingCode.Check(code, _certificate.Fingerprint(), theirs);

        if (!PairingCode.Matches(check, ask.Detail))
        {
            await PeerFraming
                .WriteAsync(stream, me with { Refused = true }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await PeerFraming
            .WriteAsync(stream, me with { Ask = PeerAsk.Pair, Detail = check }, cancellationToken)
            .ConfigureAwait(false);

        // Used once. The code was read aloud across a room and is no more secret
        // than that; leaving it live afterwards would make every later pairing
        // free to whoever heard it.
        Offering = null;

        Paired?.Invoke(
            this, new PeerPairing(true, ask.MachineId, ask.Name, theirs, string.Empty));
    }

    private async Task DecisionsAsync(
        SslStream stream, PeerMessage me, string theirs, CancellationToken cancellationToken)
    {
        if (theirs.Length == 0 || !_isPaired(theirs))
        {
            await PeerFraming
                .WriteAsync(stream, me with { Refused = true }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        DecisionSet mine = await _mine().ConfigureAwait(false);

        using var payload = new MemoryStream();
        await DecisionSetFile
            .WriteAsync(payload, mine.WithoutProposals(), cancellationToken)
            .ConfigureAwait(false);

        await PeerFraming
            .WriteAsync(stream, me with { Ask = PeerAsk.Decisions }, cancellationToken)
            .ConfigureAwait(false);

        await PeerFraming
            .WriteAsync(stream, payload.ToArray(), cancellationToken)
            .ConfigureAwait(false);
    }
}
