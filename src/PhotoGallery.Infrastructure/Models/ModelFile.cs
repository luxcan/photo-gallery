using System.Security.Cryptography;

namespace PhotoGallery.Infrastructure.Models;

/// <summary>
/// Whether a file on disk is byte-for-byte the model it claims to be.
/// </summary>
/// <remarks>
/// Separate from <see cref="FileModelStore"/> so that a graph not yet in the
/// manifest - the eye landmarks while PRP 14 is still measuring - is proved the
/// same way, by the same code, before anything opens it.
/// </remarks>
public static class ModelFile
{
    /// <summary>
    /// True when the file has exactly this length and this SHA-256 digest.
    /// </summary>
    /// <remarks>
    /// Length first, because it rejects a truncated download without reading
    /// 166 MB to reach the same conclusion. A file that cannot be read is
    /// indistinguishable from the wrong one, and both mean the same thing: do
    /// not use it.
    /// </remarks>
    public static bool Matches(string path, long bytes, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        try
        {
            if (new FileInfo(path).Length != bytes)
            {
                return false;
            }

            using FileStream stream = File.OpenRead(path);
            return string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(stream)),
                sha256,
                StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
