namespace PhotoGallery.Application.Ports;

/// <summary>
/// What one original had to say about where it was taken.
/// </summary>
/// <param name="Outcome">Which of the three answers this is.</param>
/// <param name="Latitude">Degrees north, meaningful only when <see cref="CoordinateOutcome.Found"/>.</param>
/// <param name="Longitude">Degrees east, meaningful only when <see cref="CoordinateOutcome.Found"/>.</param>
public readonly record struct CoordinateReading(
    CoordinateOutcome Outcome, double Latitude, double Longitude)
{
    /// <summary>The file carries these coordinates.</summary>
    public static CoordinateReading At(double latitude, double longitude) =>
        new(CoordinateOutcome.Found, latitude, longitude);

    /// <summary>The file was read and carries none.</summary>
    public static CoordinateReading None { get; } = new(CoordinateOutcome.None, 0d, 0d);

    /// <summary>The file could not be read, so nothing is known.</summary>
    public static CoordinateReading Unreadable { get; } = new(CoordinateOutcome.Unreadable, 0d, 0d);

    /// <summary>Whether this reading settles the question, however it settles it.</summary>
    /// <remarks>
    /// Both <see cref="CoordinateOutcome.Found"/> and <see cref="CoordinateOutcome.None"/>
    /// are worth writing down. Only the third is not.
    /// </remarks>
    public bool IsSettled => Outcome is CoordinateOutcome.Found or CoordinateOutcome.None;
}
