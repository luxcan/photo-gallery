namespace PhotoGallery.Application.Ports;

/// <summary>
/// Reads where a photograph was taken, from the original file's own metadata.
/// </summary>
/// <remarks>
/// Separate from <see cref="IThumbnailGenerator"/>, which also reads coordinates
/// but only as a by-product of preparing a picture it was going to read anyway.
/// This exists for the photographs that were prepared before the app knew to
/// look - they already have their previews, so that pass will never open them
/// again, and nothing else would ever ask.
///
/// <para>Reads the header and not the pixels. Measured over the share the
/// difference is smaller than it sounds - 738 ms a file against 844 - because
/// the cost is the round trip rather than the decoding. It is still the right
/// shape: this wants a tag, not an image, and decoding eleven thousand
/// photographs to reach one would be a waste that grows with the library.</para>
/// </remarks>
public interface IOriginalCoordinates
{
    /// <summary>
    /// Reads one file. Never throws for an unreadable file; says so instead.
    /// </summary>
    /// <remarks>
    /// The caller is expected to have established that the file's source is
    /// reachable. This cannot tell an absent file from an absent share and does
    /// not try to - it reports <see cref="CoordinateOutcome.Unreadable"/> for
    /// both, which is safe precisely because it is never written down.
    /// </remarks>
    CoordinateReading Read(string fullPath);
}
