namespace PhotoGallery.Application.Ports;

/// <summary>What one video had to say when it was opened.</summary>
/// <param name="Outcome">Which of the three answers this is.</param>
/// <param name="Video">
/// The frames and what came with them, meaningful only when
/// <see cref="KeyframeOutcome.Extracted"/>.
/// </param>
public readonly record struct KeyframeReading(KeyframeOutcome Outcome, ExtractedVideo? Video)
{
    /// <summary>Frames were taken.</summary>
    public static KeyframeReading From(ExtractedVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);
        return new KeyframeReading(KeyframeOutcome.Extracted, video);
    }

    /// <summary>The file was reached and will not decode here. Settled.</summary>
    public static KeyframeReading Undecodable { get; } =
        new(KeyframeOutcome.Undecodable, null);

    /// <summary>The file could not be reached. Not an answer, and not recorded.</summary>
    public static KeyframeReading Unavailable { get; } =
        new(KeyframeOutcome.Unavailable, null);

    /// <summary>
    /// Whether this settles the question, however it settles it.
    /// </summary>
    /// <remarks>
    /// Both a clip that gave frames and one that will never give any are worth
    /// writing down. Only the third is not - and writing it down anyway is the
    /// bug this type exists to make impossible.
    /// </remarks>
    public bool IsSettled =>
        Outcome is KeyframeOutcome.Extracted or KeyframeOutcome.Undecodable;
}
