namespace PhotoGallery.Application.UseCases.People;

/// <summary>Progress of checking everyone against the library again.</summary>
public readonly record struct RecheckProgress(int Done, int Total, string Person, int Proposed)
{
    public double Fraction => Total == 0 ? 1d : (double)Done / Total;
}
