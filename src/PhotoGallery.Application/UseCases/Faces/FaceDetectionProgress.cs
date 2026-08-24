namespace PhotoGallery.Application.UseCases.Faces;

/// <summary>Progress of a face pass.</summary>
public readonly record struct FaceDetectionProgress(int Done, int Total, int FacesFound, int Failed)
{
    public double Fraction => Total == 0 ? 1d : (double)Done / Total;
}
