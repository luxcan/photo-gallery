namespace PhotoGallery.Application.Ports;

/// <summary>Takes a few stills out of a video, and its length with them.</summary>
/// <remarks>
/// A port rather than a class because the decision behind it is not settled.
/// Windows Media Foundation is present on every target machine and needs nothing
/// bundled, but what it will decode depends on the codecs installed - AVCHD and
/// Matroska are the doubtful ones, and this library holds both. FFmpeg would
/// decode all of them and costs a large binary inside a single-file executable
/// and a licence to honour. Behind here, that choice can change without anything
/// above it moving.
///
/// <para>Shaped like <see cref="IOriginalCoordinates"/> rather than
/// <see cref="IThumbnailGenerator"/>, and that is the whole point of it: it
/// answers with an outcome instead of null, so the pass can tell a container
/// this machine cannot decode from a share that blinked. It never throws for one
/// bad file, so one unplayable clip among thousands cannot stop the pass.</para>
/// </remarks>
public interface IKeyframeExtractor
{
    /// <summary>
    /// Opens <paramref name="originalPath"/> once and returns its frames, or
    /// says why it could not.
    /// </summary>
    /// <remarks>
    /// <see cref="KeyframeOutcome.Undecodable"/> is a promise, so give it only
    /// where the file was genuinely reached and genuinely will not decode: the
    /// pass records it and never opens that file again.
    /// <see cref="KeyframeOutcome.Unavailable"/> is the safe answer whenever
    /// there is doubt.
    ///
    /// <para>A clip that decodes but will not report its length is not a
    /// failure, and must come back <see cref="KeyframeOutcome.Extracted"/> with
    /// a null duration.</para>
    /// </remarks>
    Task<KeyframeReading> ExtractAsync(
        string originalPath, CancellationToken cancellationToken = default);
}
