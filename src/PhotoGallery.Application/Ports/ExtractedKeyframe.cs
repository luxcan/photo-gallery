namespace PhotoGallery.Application.Ports;

/// <summary>One still taken from a video, in the two rendition sizes.</summary>
/// <remarks>
/// The same pair of sizes a photograph yields, and for the same reasons: the
/// tile is what the grid draws, and the preview is what the face detector reads.
/// Producing both from the one decoded frame costs nothing beyond the encode.
/// </remarks>
/// <param name="Position">Where in the clip this frame was taken from.</param>
public sealed record ExtractedKeyframe(TimeSpan Position, byte[] Tile, byte[] Preview)
{
    public int TotalBytes => Tile.Length + Preview.Length;
}
