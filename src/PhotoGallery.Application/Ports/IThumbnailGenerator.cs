namespace PhotoGallery.Application.Ports;

/// <summary>Builds a preview image from an original photo.</summary>
public interface IThumbnailGenerator
{
    /// <summary>
    /// Decodes <paramref name="originalPath"/> once and returns both renditions,
    /// or null when the file cannot be decoded.
    /// </summary>
    /// <remarks>
    /// Returning null rather than throwing: a library of twenty thousand files
    /// will contain some that are corrupt, truncated or in a format Windows has
    /// no codec for, and one of those must not stop the pass.
    /// </remarks>
    /// <param name="rotation">
    /// A further quarter turn clockwise, applied after the file's own EXIF
    /// orientation. Applied here rather than afterwards so the picture is turned
    /// while it is already decoded, instead of being read back and encoded twice.
    /// </param>
    Task<GeneratedThumbnail?> GenerateAsync(
        string originalPath, int rotation = 0, CancellationToken cancellationToken = default);
}
