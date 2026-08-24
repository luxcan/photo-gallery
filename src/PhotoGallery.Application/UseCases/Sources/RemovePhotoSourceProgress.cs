namespace PhotoGallery.Application.UseCases.Sources;

/// <summary>Progress of a detach, reported as the records go.</summary>
/// <param name="Done">Records dealt with, whether or not their files went.</param>
/// <param name="Reclaimed">
/// Pairs of files deleted. Lower than <paramref name="Done"/> when pictures were
/// duplicated or never prepared, since identical copies share one rendition.
/// </param>
/// <param name="Failed">
/// Records whose files something else is holding. They keep their rows, so the
/// detach can be run again once whatever holds them lets go.
/// </param>
public readonly record struct RemovePhotoSourceProgress(
    int Done, int Total, int Reclaimed, int Failed)
{
    public double Fraction => Total == 0 ? 1d : (double)Done / Total;
}
