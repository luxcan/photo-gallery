namespace PhotoGallery.Application.Ports;

/// <summary>
/// The two rendition sizes, in one place so the trade-off is visible.
/// </summary>
public static class ThumbnailSizes
{
    /// <summary>
    /// Grid tile. 400px keeps a 200px cell crisp even at 200% Windows scaling,
    /// which is as large as a tile gets before the view should be showing the
    /// preview instead.
    /// </summary>
    /// <remarks>
    /// Measured over 200 photos of the real library: 21 KB each, so about
    /// 0.23 GB for 11,481 photos. Dropping to 320px would save only 0.07 GB and
    /// go soft on a high-DPI screen, so it is not worth it.
    /// </remarks>
    public const int TileEdge = 400;

    public const int TileQuality = 78;

    /// <summary>
    /// Single-photo view, and the input face detection will want - it needs
    /// enough resolution to find a small face in a group shot.
    /// </summary>
    /// <remarks>
    /// This, not the tile, is what fills the disk: 127 KB each, about 1.39 GB
    /// for the same library - six times the tiles put together.
    /// </remarks>
    public const int PreviewEdge = 1024;

    public const int PreviewQuality = 82;
}
