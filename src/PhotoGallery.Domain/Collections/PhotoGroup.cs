namespace PhotoGallery.Domain.Collections;

/// <summary>
/// One occasion the clusterer found: which photographs, when, and what sort of
/// occasion it is.
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="Collection"/>. This is the answer to "what
/// belongs together", with no name, no cover and no row - the pass turns it
/// into one of those, and has to consult what the user has already said before
/// it does.
/// </remarks>
public sealed record PhotoGroup(
    IReadOnlyList<int> AssetIds,
    DateTime StartUtc,
    DateTime EndUtc,
    CollectionKind Kind)
{
    /// <summary>How this occasion is remembered once its row is gone.</summary>
    public string Key => ProposalKey.Of(StartUtc, EndUtc);

    /// <summary>How many days it covers, first and last inclusive.</summary>
    public int Days =>
        DateOnly.FromDateTime(EndUtc).DayNumber - DateOnly.FromDateTime(StartUtc).DayNumber + 1;
}
