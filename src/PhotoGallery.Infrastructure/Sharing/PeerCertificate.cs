using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// This machine's own certificate for talking to the others directly, made once
/// and kept in the working folder.
/// </summary>
/// <remarks>
/// Self-signed, and there is nothing wrong with that here. A certificate
/// authority answers "is this machine who the internet says it is", which is not
/// a question a family network can ask or needs to: what matters is that the
/// laptop somebody paired with last week is the same laptop today, and a
/// fingerprint remembered at pairing answers exactly that.
///
/// <para>Kept rather than made per run, because the fingerprint <em>is</em> the
/// identity. A certificate regenerated on every start would mean pairing again
/// every start, which is a feature nobody would use twice.</para>
/// </remarks>
public sealed class PeerCertificate
{
    private const string FileName = "machine.pfx";

    /// <summary>
    /// Not a secret, and deliberately not treated as one.
    /// </summary>
    /// <remarks>
    /// The file sits in the working folder beside the database, which holds
    /// every name in the library. Anybody who can read one can read the other,
    /// so a password here would protect nothing and would have to be kept
    /// somewhere - which is the same problem one layer down.
    /// </remarks>
    private const string Password = "photogallery";

    private readonly IWorkingFolder _workingFolder;
    private X509Certificate2? _mine;

    public PeerCertificate(IWorkingFolder workingFolder) => _workingFolder = workingFolder;

    /// <summary>This machine's certificate, made on first use.</summary>
    public X509Certificate2 Mine()
    {
        if (_mine is not null)
        {
            return _mine;
        }

        string path = Path.Combine(_workingFolder.Root, FileName);

        if (File.Exists(path))
        {
            try
            {
                return _mine = X509CertificateLoader.LoadPkcs12FromFile(
                    path, Password, X509KeyStorageFlags.Exportable);
            }
            catch (CryptographicException)
            {
                // Unreadable, so it is not an identity any more. Made again
                // rather than refused: the cost is pairing once more, and the
                // alternative is a feature that never works again.
                File.Delete(path);
            }
        }

        // Made, written, and then loaded back rather than used directly. On
        // Windows a certificate straight out of CreateSelfSigned has a key
        // SChannel will not accept for a TLS handshake - the connection fails
        // with an unexplained end of stream - and a round trip through PKCS#12
        // is what associates it properly. It also means the certificate used
        // this run is the same object every later run will load.
        using X509Certificate2 fresh = Make();
        byte[] pkcs12 = fresh.Export(X509ContentType.Pkcs12, Password);
        File.WriteAllBytes(path, pkcs12);

        return _mine = X509CertificateLoader.LoadPkcs12(
            pkcs12, Password, X509KeyStorageFlags.Exportable);
    }

    /// <summary>
    /// What this machine's certificate is called when two machines compare
    /// notes.
    /// </summary>
    /// <remarks>
    /// The SHA-256 of the certificate itself, lower case and unpunctuated, so
    /// that two machines computing it from the same bytes agree without having
    /// to agree on a format first.
    /// </remarks>
    public string Fingerprint() => FingerprintOf(Mine());

    /// <summary>The same, for a certificate that arrived from somewhere else.</summary>
    public static string FingerprintOf(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return Convert.ToHexStringLower(SHA256.HashData(certificate.RawData));
    }

    private static X509Certificate2 Make()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=Photo Gallery", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

        // Both, because the same certificate is used at each end: this machine
        // serves its answers and asks for everybody else's over connections it
        // did not necessarily start.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], false));

        // Twenty years. The expiry is not what secures this - the fingerprint
        // is - and a certificate that lapsed would unpair the whole house on a
        // date nobody had written down.
        DateTimeOffset from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        return request.CreateSelfSigned(from, from.AddYears(20));
    }
}
