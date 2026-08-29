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

        return string.Concat(digest.Length <= Length ? digest : digest[..Length], Extension);
    }
}
