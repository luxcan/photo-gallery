namespace PhotoGallery.Application.Ports;

/// <summary>One indexed picture and the file it came from.</summary>
/// <param name="FullPath">
/// Built from the source's root, so it is where the file is now rather than
/// where it was when the row was written.
/// </param>
/// <param name="SourceRoot">
/// The root <paramref name="FullPath"/> was built from. Turning asks whether it
/// can be reached before touching anything, since two rows sharing a rendition
/// can belong to different sources and only one of them may be away.
/// </param>
public readonly record struct AssetFile(int AssetId, string FullPath, string SourceRoot);
