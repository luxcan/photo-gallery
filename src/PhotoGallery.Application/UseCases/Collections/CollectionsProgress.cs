namespace PhotoGallery.Application.UseCases.Collections;

/// <summary>Progress of the grouping phase.</summary>
public readonly record struct CollectionsProgress(int Done, int Total)
{
    public double Fraction => Total == 0 ? 1d : (double)Done / Total;
}
