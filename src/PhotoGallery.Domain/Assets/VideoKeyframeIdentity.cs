using System.Security.Cryptography;
using System.Text;

namespace PhotoGallery.Domain.Assets;

/// <summary>
/// The name a video's frame is stored under, worked out before it is decoded.
/// </summary>
/// <remarks>
/// Photographs name their renditions after a hash of their own bytes, which is
/// what lets two copies of one picture share a file. A video cannot afford that:
/// hashing this library's videos means reading 267 GB over a 6.4 MB/s link, and
/// the whole point of extracting a few frames is that it seeks instead of
/// reading the file through.
///
/// <para>So identity comes from what a scan already knows for free - where the
/// file is, how big it is, and when it changed - plus which frame of it this is.
/// That is not content identity and does not dedupe two copies of the same clip,
/// which is a price worth paying: it costs a few duplicated frames among 4,743
/// videos, and saves eleven hours of reading.</para>
///
/// <para>It does hold the property that actually matters. A video whose bytes
/// change gets a new length or a new modified time, so its frames get new names
/// and are rebuilt; a video that has not changed resolves to the same names, so
/// running the pass again overwrites its own frames rather than leaving a second
/// set beside them.</para>
/// </remarks>
public static class VideoKeyframeIdentity
{
    /// <summary>
    /// A stable hex digest for one frame of one video, in the shape the
    /// thumbnail store names its files after.
    /// </summary>
    /// <param name="relativePath">The asset's path below its source's root.</param>
    /// <param name="length">The file's size in bytes.</param>
    /// <param name="modifiedUtc">The file's last modified time.</param>
    /// <param name="ordinal">Which frame of the clip this is, 0 first.</param>
    public static string For(string relativePath, long length, DateTime modifiedUtc, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);

        // Invariant and case-folded: the same file reached through a share and
        // through a drive letter differs in case on Windows and is the same
        // video, and a name that moved between machines should not rebuild.
        string seed = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{relativePath.ToUpperInvariant()}|{length}|{modifiedUtc.Ticks}|{ordinal}");

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
    }
}
