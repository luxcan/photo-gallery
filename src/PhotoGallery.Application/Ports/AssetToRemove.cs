namespace PhotoGallery.Application.Ports;

/// <summary>One picture, and what deleting it would cost.</summary>
/// <param name="Faces">
/// How many faces were found in it. Lost with the picture, which is fair - they
/// describe a photograph that will not be there.
/// </param>
/// <param name="Names">
/// How many of those faces somebody confirmed a name for. Counted separately
/// because it is the part that took the user's own time, and the part they would
/// most regret losing without being told.
/// </param>
/// <param name="OtherCopies">
/// How many other rows draw the same rendition. While any remain the cached
/// pictures have to stay, or those rows would go blank for the sake of this one.
/// </param>
/// <param name="SourceRoot">
/// The root of the source this file belongs to. Carried because deleting must
/// ask whether that root can be reached before it believes anything the file
/// itself says about being absent.
/// </param>
public sealed record AssetToRemove(
    int AssetId,
    string FileName,
    string FullPath,
    string SourceRoot,
    string? ThumbnailName,
    int Faces,
    int Names,
    int OtherCopies);
