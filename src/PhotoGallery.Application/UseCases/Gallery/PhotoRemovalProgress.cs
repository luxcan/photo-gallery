namespace PhotoGallery.Application.UseCases.Gallery;

/// <summary>How far a deletion has got, and which photograph it is on.</summary>
/// <param name="Done">Photographs already finished with, deleted or refused.</param>
/// <param name="FileName">
/// The picture being deleted now, not the one just finished. Reported before the
/// file goes, because a screen naming what has already gone says nothing about
/// what it is doing.
/// </param>
/// <param name="ThumbnailName">
/// The cached copy the shell can draw, or null for a picture that was never
/// prepared. It is still on disk when this is reported and gone a moment later,
/// which is the whole reason the name travels with the report rather than being
/// looked up afterwards.
/// </param>
public readonly record struct PhotoRemovalProgress(
    int Done, int Total, string FileName, string? ThumbnailName)
{
    public double Fraction => Total == 0 ? 1d : (double)Done / Total;
}
