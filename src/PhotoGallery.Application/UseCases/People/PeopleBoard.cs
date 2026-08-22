using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>Everything the People screen shows at once.</summary>
public sealed record PeopleBoard(
    IReadOnlyList<PersonSummary> People,
    int TotalFaces,
    int NamedFaces)
{
    public static PeopleBoard Empty { get; } = new([], 0, 0);

    public bool HasFaces => TotalFaces > 0;
}
