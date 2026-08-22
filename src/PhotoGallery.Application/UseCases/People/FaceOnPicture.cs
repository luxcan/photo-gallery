using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// Treats the same face in the same picture as one thing, however many files
/// that picture exists as.
/// </summary>
/// <remarks>
/// Renditions are named after the picture's content, so two files with the same
/// rendition name are the same photograph - and a face at the same place in it
/// is the same face. Showing each copy would ask the user the same question
/// several times over; on this library one photograph exists as eight files.
///
/// <para>Only for what is shown. Every copy still gets its own row and its own
/// name, because each is a real file the user can open.</para>
/// </remarks>
public static class FaceOnPicture
{
    public static IEqualityComparer<(string ThumbnailName, FaceBounds Bounds)> Comparer { get; } =
        new SamePlaceInTheSamePicture();

    private sealed class SamePlaceInTheSamePicture
        : IEqualityComparer<(string ThumbnailName, FaceBounds Bounds)>
    {
        public bool Equals(
            (string ThumbnailName, FaceBounds Bounds) left,
            (string ThumbnailName, FaceBounds Bounds) right) =>
            left.Bounds == right.Bounds
            && string.Equals(
                left.ThumbnailName, right.ThumbnailName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string ThumbnailName, FaceBounds Bounds) value) =>
            HashCode.Combine(
                value.ThumbnailName.ToLowerInvariant(),
                value.Bounds.X,
                value.Bounds.Y,
                value.Bounds.Width,
                value.Bounds.Height);
    }
}
