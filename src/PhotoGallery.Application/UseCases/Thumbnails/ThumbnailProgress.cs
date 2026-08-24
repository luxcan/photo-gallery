namespace PhotoGallery.Application.UseCases.Thumbnails;

/// <summary>Progress of a thumbnail pass.</summary>
public readonly record struct ThumbnailProgress(int Done, int Total, int Built, int Failed)
{
    public double Fraction => Total == 0 ? 1d : (double)Done / Total;
}
