namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// What one photograph or video is called on every machine that can see it: the
/// source the two libraries matched, and the path below its root.
/// </summary>
/// <remarks>
/// The path, because that is already what the app itself means by "the same
/// file". A scan holds what it knows in a dictionary keyed on the relative path;
/// length and modified time are read only to decide whether a file has
/// <em>changed</em>, never which file it is. A photograph moved to another
/// folder is therefore already, today, a row removed and a row added.
///
/// <para>An earlier draft keyed decisions on the content hash, which survives
/// more - and that is the problem. Sharing must not have a stronger notion of
/// identity than the scan it rides on, or the two disagree about what happened
/// whenever a file moves, and the divergence is invisible. The hash keeps its
/// own job: it is what a rendition file is named after. <strong>The path says
/// which photograph, the hash says which picture.</strong></para>
///
/// <para>Never the root itself. <c>\\192.168.50.103\PhotoGallery</c> on one
/// laptop and <c>Z:\</c> on another are the same folder reached two ways, so a
/// key holding machine-local text would lock out a family member for doing the
/// normal thing Windows offers to do for them. The two sources are paired once
/// and share a <see cref="Library.PhotoSource.SharedId"/>, and that is what
/// scopes a decision.</para>
///
/// <para>Separators are unified and case is ignored, because neither is a fact
/// about which photograph this is. Windows treats <c>a\b.jpg</c> and
/// <c>a/b.jpg</c> as one path and <c>IMG.JPG</c> and <c>img.jpg</c> as one file;
/// a key that disagreed would hold two answers about one picture and match
/// neither.</para>
/// </remarks>
public readonly struct AssetKey : IEquatable<AssetKey>
{
    /// <summary>
    /// The identity the two matched sources share. <see cref="Guid.Empty"/> is
    /// not a source and never matches one.
    /// </summary>
    public Guid SharedSourceId { get; }

    /// <summary>
    /// Path below the source's root, e.g. <c>20230203 - Chingay\IMG_6769.MOV</c>,
    /// with separators unified to backslashes.
    /// </summary>
    public string RelativePath { get; }

    public AssetKey(Guid sharedSourceId, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        SharedSourceId = sharedSourceId;
        RelativePath = Normalise(relativePath);
    }

    /// <summary>
    /// The same path as this key holds it: separators unified, ends trimmed.
    /// </summary>
    /// <remarks>
    /// Public because a caller matching keys against rows read from the index
    /// has to put those rows into the same shape, and doing it a second way is
    /// how the two drift apart.
    /// </remarks>
    public static string Normalise(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        return relativePath.Replace('/', '\\').Trim('\\');
    }

    public bool Equals(AssetKey other) =>
        SharedSourceId.Equals(other.SharedSourceId)
        && string.Equals(RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is AssetKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            SharedSourceId,
            StringComparer.OrdinalIgnoreCase.GetHashCode(RelativePath ?? string.Empty));

    /// <summary>
    /// The key as one string, which is how it is written to a file.
    /// </summary>
    /// <remarks>
    /// A colon separates the two halves because Windows will not have one in a
    /// path - it is reserved for the drive and for alternate streams - so the
    /// free-text half cannot contain the separator and the split needs no
    /// escaping. The identity half never does either.
    /// </remarks>
    public override string ToString() => $"{SharedSourceId:D}:{RelativePath}";

    /// <summary>Reads back what <see cref="ToString"/> wrote.</summary>
    /// <exception cref="FormatException">The text is not a key.</exception>
    public static AssetKey Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int cut = text.IndexOf(':', StringComparison.Ordinal);

        if (cut <= 0 || cut == text.Length - 1 || !Guid.TryParse(text[..cut], out Guid source))
        {
            throw new FormatException($"Not a photograph key: {text}");
        }

        return new AssetKey(source, text[(cut + 1)..]);
    }

    public static bool operator ==(AssetKey left, AssetKey right) => left.Equals(right);

    public static bool operator !=(AssetKey left, AssetKey right) => !left.Equals(right);
}
