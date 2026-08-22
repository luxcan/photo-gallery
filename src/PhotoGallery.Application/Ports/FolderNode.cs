namespace PhotoGallery.Application.Ports;

/// <summary>One folder of a photo source, and what it holds.</summary>
/// <remarks>
/// Folder names carry the meaning in this library - "20200214_Ana Lim Born",
/// "20230203 - Chingay" - so the tree is how an event is found, not a
/// decoration. <see cref="ItemCount"/> counts everything beneath the folder as
/// well as in it, because that is what selecting it shows.
/// </remarks>
public sealed record FolderNode(
    int PhotoSourceId,
    string RelativeFolder,
    string Name,
    int ItemCount,
    IReadOnlyList<FolderNode> Children);
