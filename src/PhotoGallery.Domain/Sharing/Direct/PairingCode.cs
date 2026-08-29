using System.Security.Cryptography;
using System.Text;

namespace PhotoGallery.Domain.Sharing.Direct;

/// <summary>
/// The six digits shown on one machine and typed on the other, and the check
/// value that binds them to the channel.
/// </summary>
/// <remarks>
/// <strong>The code alone proves nothing.</strong> Somebody else on the Wi-Fi
/// can watch the offer, answer it first and read the six digits over the
/// person's shoulder or simply guess them one in a million times. What makes the
/// pairing safe is that both sides derive a check value from the code
/// <em>and both certificate fingerprints</em>: a machine in the middle has its
/// own certificate, so its check value cannot match, and the two ends see the
/// mismatch and stop.
///
/// <para>Six digits rather than a passphrase because it is read aloud across a
/// room, once, and never again - the peer is remembered by fingerprint
/// afterwards. A fingerprint that changes means pairing again rather than a
/// silent accept.</para>
/// </remarks>
public static class PairingCode
{
    /// <summary>How many digits the code has.</summary>
    public const int Digits = 6;

    /// <summary>
    /// A fresh code, from the system's cryptographic generator.
    /// </summary>
    /// <remarks>
    /// Not a plain random: a predictable code would let somebody who knows when
    /// the offer was made answer it without ever seeing the screen.
    /// </remarks>
    public static string Mint() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", null);

    /// <summary>
    /// The value both ends compute and compare, from the code and both
    /// certificates.
    /// </summary>
    /// <remarks>
    /// The fingerprints are sorted before hashing so that the offering machine
    /// and the accepting one reach the same answer without having to agree which
    /// of them is which - there is no first machine anywhere else in this
    /// feature and there is no reason to invent one here.
    /// </remarks>
    public static string Check(string code, string oneFingerprint, string otherFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(oneFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(otherFingerprint);

        string[] both = [Tidy(oneFingerprint), Tidy(otherFingerprint)];
        Array.Sort(both, StringComparer.Ordinal);

        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{code.Trim()}|{both[0]}|{both[1]}")));
    }

    /// <summary>
    /// Whether two check values are the same, compared without leaking where
    /// they differ.
    /// </summary>
    /// <remarks>
    /// A plain string comparison returns as soon as two characters differ, and
    /// how long that took is a measurable fact about how much of the value was
    /// right. That matters here because an attacker can retry: the code is six
    /// digits and the offer can be answered again.
    /// </remarks>
    public static bool Matches(string mine, string theirs) =>
        mine is not null
        && theirs is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(mine), Encoding.UTF8.GetBytes(theirs));

    /// <summary>Whether a typed code could be one at all, before anything is sent.</summary>
    public static bool IsWellFormed(string? code) =>
        code is not null
        && code.Trim().Length == Digits
        && code.Trim().All(char.IsAsciiDigit);

    private static string Tidy(string fingerprint) =>
        fingerprint.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
