namespace PhotoGallery.Application.UseCases.Albums;

/// <summary>Progress of the grouping phase.</summary>
public readonly record struct AlbumsProgress(int Done, int Total)
{
    public double Fraction => Total == 0 ? 1d : (double)Done / Total;
}
