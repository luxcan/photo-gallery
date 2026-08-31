namespace PhotoGallery.Domain.Assets;

/// <summary>
/// What a rendition of some content is called, wherever that question is asked.
/// </summary>
/// <remarks>
/// One rule in one place because three things ask it and they must agree
/// exactly: the store that writes the files, the pool that copies them between
/// machines, and the row that records which one a photograph has. Two of them
/// disagreeing would not fail - it would fetch a name nothing had written, and
/// report a library complete with a photograph whose picture is not there.
///
/// <para>Thirty-two characters of the digest, which is 128 bits: enough that two
/// different pictures colliding is not a thing that happens, and short enough
/// that a path stays a path.</para>
/// </remarks>
public static class RenditionName
{
    private const int Length = 32;

    private const string Extension = ".jpg";

    /// <summary>The file name for a digest, whether of bytes or of a video frame.</summary>
    public static string For(string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);

        string name = string.Concat(digest.Length <= Length ? digest : digest[..Length], Extension);
        return IsSafeFileName(name)
            ? name
            : throw new ArgumentException("The digest cannot form a safe rendition name.", nameof(digest));
    }

    /// <summary>
    /// Whether a name can identify one rendition without also identifying a path.
    /// </summary>
    /// <remarks>
    /// Manifest files come from other machines. Treating their name as a path
    /// would let a separator, rooted path or alternate data stream escape the
    /// thumbnail folder when the rendition is copied.
    /// </remarks>
    public static bool IsSafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 128
            || name.IndexOfAny(['\\', '/', ':']) >= 0
            || !Path.GetExtension(name).Equals(Extension, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        string stem = Path.GetFileNameWithoutExtension(name);
        return stem.Length > 0
            && char.IsAsciiLetterOrDigit(stem[0])
            && stem.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            && !stem.EndsWith("-p", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns a safe rendition name, or rejects the path-like value.</summary>
    public static string RequireSafeFileName(string? name, string? parameterName = null) =>
        IsSafeFileName(name)
            ? name!
            : throw new ArgumentException(
                "A rendition name must be a single JPEG file name.", parameterName ?? nameof(name));
}
