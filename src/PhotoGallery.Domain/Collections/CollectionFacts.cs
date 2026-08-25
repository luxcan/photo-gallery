namespace PhotoGallery.Domain.Collections;

/// <summary>
/// Everything known about one occasion by the time it needs a name.
/// </summary>
/// <param name="Places">
/// The places its photographs resolved to, commonest first. Usually empty:
/// coordinates are on about one photograph in nine.
/// </param>
/// <param name="People">
/// The people named in it, commonest first. Only ever people the user named
/// themselves - the app does not invent a person to put in a title.
/// </param>
public sealed record CollectionFacts(
    CollectionKind Kind,
    DateTime StartUtc,
    DateTime EndUtc,
    IReadOnlyList<string> Places,
    IReadOnlyList<string> People,
    int PhotoCount)
{
    public int Days =>
        DateOnly.FromDateTime(EndUtc).DayNumber - DateOnly.FromDateTime(StartUtc).DayNumber + 1;
}
