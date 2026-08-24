using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Domain.Search;

/// <summary>
/// What one photograph is of, as the vector a typed description is compared
/// against.
/// </summary>
/// <remarks>
/// A table of its own rather than another column on <see cref="Asset"/>, and the
/// reason is measured rather than tidy-minded: the vector is three kilobytes, and
/// the duplicate pass alone materialises every photo row in the library. Hanging
/// it off the asset would put 35 MB behind every query that wanted a file's
/// length. The faces feature learned this the expensive way - reading every
/// vector to answer a screen that never compares one.
///
/// <para>The row's existence is the resumability marker, which faces could not
/// do: a photograph with no faces in it is indistinguishable from one never
/// looked at, but every picture has exactly one answer to "what is this of", so
/// having a row means having been indexed.</para>
/// </remarks>
public sealed class PhotoContent
{
    /// <summary>The photograph, and the key: one vector per picture.</summary>
    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    public ContentEmbedding Vector { get; set; }

    /// <summary>When the rendition was read to produce this.</summary>
    public DateTime IndexedUtc { get; set; }

    /// <summary>
    /// The rendition it was taken from.
    /// </summary>
    /// <remarks>
    /// Recorded so the pass can tell that two rows sharing one preview share an
    /// answer, and read it once for both. Renditions are named after the
    /// picture's content, so this changes exactly when the picture does.
    /// </remarks>
    public required string ThumbnailName { get; set; }
}
