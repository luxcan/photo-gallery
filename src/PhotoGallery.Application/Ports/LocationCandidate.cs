namespace PhotoGallery.Application.Ports;

/// <summary>
/// One photograph whose location has not been worked out yet.
/// </summary>
/// <remarks>
/// Carries the coordinates rather than only the path, which is the whole reason
/// this is not <see cref="AssetFile"/>. A photograph prepared since the app
/// learnt to read GPS already has its coordinates on the row and needs no file
/// opened at all - only a gazetteer lookup, which is arithmetic. Handing the
/// pass a path alone would have it re-read those originals over the share to
/// learn something already written down, and would make the pass need the share
/// for work that does not.
/// </remarks>
/// <param name="FullPath">Where the file is now, built from its source's root.</param>
/// <param name="SourceRoot">
/// The root that path was built from, so the pass can ask whether it is reachable
/// before opening anything.
/// </param>
/// <param name="Latitude">Already known, or null when the file has yet to be read.</param>
public readonly record struct LocationCandidate(
    int AssetId,
    string FullPath,
    string SourceRoot,
    double? Latitude,
    double? Longitude)
{
    /// <summary>Whether the coordinates are already in hand, so no file need be opened.</summary>
    public bool IsAlreadyRead => Latitude is not null && Longitude is not null;
}
