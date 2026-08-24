namespace PhotoGallery.Application.UseCases.People;

/// <summary>What checking everyone again turned up.</summary>
public sealed record RecheckResult(int People, int Proposed)
{
    public string Summary => People == 0
        ? "nobody has been named yet, so there is nothing to look for"
        : Proposed == 0
            ? $"checked {People:N0} people; nothing new was found"
            : $"checked {People:N0} people and found {Proposed:N0} faces to look at";
}
