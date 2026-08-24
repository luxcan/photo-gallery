namespace PhotoGallery.Application.UseCases.Faces;

/// <summary>
/// One preview to read, and every photo whose faces it decides.
/// </summary>
/// <remarks>
/// Renditions are named after the picture's content, so two byte-identical
/// photos share one file. Reading it twice would find the same faces twice.
/// </remarks>
internal sealed record PendingFaceScan(string PreviewPath, IReadOnlyList<int> AssetIds);
