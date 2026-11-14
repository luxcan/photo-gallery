namespace PhotoGallery.Domain.Assets;

/// <summary>
/// How sharp and how well exposed one photograph's cached preview is, for the
/// keeper ladder of PRP 14.
/// </summary>
/// <remarks>
/// Belongs to the preview it was read from, which is why the preview's name and
/// the time it was written are kept with it: turning a picture rewrites the
/// preview in place, and a measurement of the old file is measured again rather
/// than trusted. Never shared with another computer.
/// </remarks>
public sealed class PreviewQuality
{
    /// <summary>The photograph, and the key: one measurement per picture.</summary>
    public int AssetId { get; set; }

    public Asset? Asset { get; set; }

    /// <summary>The rendition it was read from.</summary>
    public required string ThumbnailName { get; set; }

    /// <summary>When that preview file had last been written, as read off the disk.</summary>
    public DateTime PreviewWrittenUtc { get; set; }

    /// <summary>The preview's width: the space its face boxes are in.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// The variance of the Laplacian over the most detailed quarter at the
    /// common size, or null when the preview is smaller than that size.
    /// </summary>
    public double? Sharpness { get; set; }

    /// <summary>The share of the preview blown white, from 0 to 1.</summary>
    public double ClippedHigh { get; set; }

    /// <summary>The share of the preview crushed black, from 0 to 1.</summary>
    public double ClippedLow { get; set; }

    public DateTime MeasuredUtc { get; set; }
}
