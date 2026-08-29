using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Domain.Sharing.Direct;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// One end of a direct connection to another computer in the house.
/// </summary>
/// <remarks>
/// <see cref="TcpListener"/> and <see cref="SslStream"/>, not
/// <c>HttpListener</c>: on Windows that will not bind anything but
/// <c>localhost</c> without a <c>netsh urlacl</c> reservation made as
/// administrator - which is an installer, which this app does not have.
///
/// <para><strong>Every certificate is accepted and every fingerprint is
/// checked.</strong> These are self-signed, so the usual chain validation would
/// refuse all of them and prove nothing if it did not: what matters on a family
/// network is that the laptop paired with last week is the same laptop today,
/// and the fingerprint answers exactly that. A fingerprint that has changed
/// means pairing again rather than a silent accept.</para>
/// </remarks>
public sealed class PeerLink : IPeerLink
{
    private readonly PeerCertificate _certificate;

    public PeerLink(PeerCertificate certificate) => _certificate = certificate;

    /// <summary>
    /// Connects, says hello, and answers what the other machine said it was.
    /// </summary>
    /// <remarks>
    /// The fingerprint comes from the certificate rather than from anything the
    /// other end claims about itself, which is the whole point: a machine can
    /// call itself anything and cannot call itself by somebody else's key.
    /// </remarks>
    public async Task<PeerFound> GreetAsync(
        string host,
        int port,
        MachineIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(identity);

        PeerMessage me = Saying(identity, PeerAsk.Hello, string.Empty);

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

        await using SslStream stream = await SecureAsync(client, host, cancellationToken)
            .ConfigureAwait(false);

        await PeerFraming.WriteAsync(stream, me, cancellationToken).ConfigureAwait(false);

        PeerMessage? them = await PeerFraming
            .ReadMessageAsync(stream, cancellationToken)
            .ConfigureAwait(false);

        return them is null
            ? throw new InvalidDataException("The other computer said nothing.")
            : new PeerFound(
                them.MachineId, them.Name, them.AppVersion, them.SchemaVersion, Theirs(stream));
    }

    /// <summary>
    /// Connects, says hello, and asks for everything that machine has decided.
    /// </summary>
    /// <remarks>
    /// The set arrives as the same gzipped document a shared folder holds, and
    /// is read by the same code. The merge cannot tell the two apart and must
    /// never learn to.
    /// </remarks>
    public async Task<DecisionSet?> AskAsync(
        string host,
        int port,
        MachineIdentity identity,
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(identity);

        PeerMessage me = Saying(identity, PeerAsk.Decisions, string.Empty);

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

        await using SslStream stream = await SecureAsync(client, host, cancellationToken)
            .ConfigureAwait(false);

        // Checked before a word is exchanged. A fingerprint that has changed is
        // a machine this one has not paired with, whatever it says its name is.
        string theirs = Theirs(stream);

        if (!string.Equals(theirs, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthenticationException(
                "That computer's identity has changed since you paired with it. "
              + "Pair with it again to be sure it is the same one.");
        }

        await PeerFraming
            .WriteAsync(stream, me with { Ask = PeerAsk.Decisions }, cancellationToken)
            .ConfigureAwait(false);

        PeerMessage? answer = await PeerFraming
            .ReadMessageAsync(stream, cancellationToken)
            .ConfigureAwait(false);

        if (answer is null || answer.Refused)
        {
            return null;
        }

        byte[]? body = await PeerFraming
            .ReadAsync(stream, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return null;
        }

        using var payload = new MemoryStream(body, writable: false);
        return await DecisionSetFile.ReadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Offers a pairing code's check value and answers whether the other end
    /// agreed.
    /// </summary>
    /// <remarks>
    /// Both ends derive the value from the code and <em>both</em> fingerprints,
    /// which is what binds the code to this particular channel: a machine in the
    /// middle has its own certificate, so its value cannot match either end's
    /// and both of them see it.
    /// </remarks>
    public async Task<PeerPairing> PairAsync(
        string host,
        int port,
        MachineIdentity identity,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(identity);

        PeerMessage me = Saying(identity, PeerAsk.Pair, string.Empty);

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

        await using SslStream stream = await SecureAsync(client, host, cancellationToken)
            .ConfigureAwait(false);

        string theirs = Theirs(stream);
        string check = PairingCode.Check(code, _certificate.Fingerprint(), theirs);

        await PeerFraming
            .WriteAsync(stream, me with { Ask = PeerAsk.Pair, Detail = check }, cancellationToken)
            .ConfigureAwait(false);

        PeerMessage? answer = await PeerFraming
            .ReadMessageAsync(stream, cancellationToken)
            .ConfigureAwait(false);

        if (answer is null)
        {
            return Refused("That computer stopped answering.");
        }

        if (answer.Refused || !PairingCode.Matches(check, answer.Detail))
        {
            // The same message either way, and deliberately: "wrong code" and
            // "somebody is in the middle" look identical from here, and telling
            // them apart is not this machine's to do.
            return Refused(
                "That code did not match. Check the six digits on the other computer, "
              + "and try again.");
        }

        return new PeerPairing(true, answer.MachineId, answer.Name, theirs, string.Empty);
    }

    /// <summary>
    /// Wraps a connection in TLS, accepting the certificate and keeping its
    /// fingerprint.
    /// </summary>
    private async Task<SslStream> SecureAsync(
        TcpClient client, string host, CancellationToken cancellationToken)
    {
        var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);

        await stream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = host,
                ClientCertificates = [_certificate.Mine()],
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,

                // Accepted, and then checked by fingerprint. These certificates
                // are self-signed by design: an authority answers a question
                // about the internet, and this is a question about the laptop in
                // the next room.
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
            cancellationToken).ConfigureAwait(false);

        return stream;
    }

    /// <summary>How this machine introduces itself, in the shape the wire uses.</summary>
    private static PeerMessage Saying(MachineIdentity me, PeerAsk ask, string detail) =>
        new(ask, me.Id, me.Name, me.AppVersion, me.SchemaVersion, detail, Refused: false);

    private static PeerPairing Refused(string problem) =>
        new(false, Guid.Empty, string.Empty, string.Empty, problem);

    private static string Theirs(SslStream stream) =>
        stream.RemoteCertificate is X509Certificate certificate
            ? PeerCertificate.FingerprintOf(
                X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()))
            : throw new AuthenticationException(
                "That computer did not identify itself.");
}


