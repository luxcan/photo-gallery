using PhotoGallery.Domain.Search;

namespace PhotoGallery.Application.Ports;

/// <summary>A photograph whose rendition has not been described yet.</summary>
public readonly record struct ContentScanCandidate(int AssetId, string ThumbnailName);

/// <summary>
/// One preview to read, and every row that shares it.
/// </summary>
/// <remarks>
/// Renditions are named after the picture's content, so duplicate files share
/// one - and one read answers for all of them. On this library that is 250
/// photographs the pass does not have to look at twice.
/// </remarks>
public sealed record PendingContentScan(
    string PreviewPath, string ThumbnailName, IReadOnlyList<int> AssetIds);

/// <summary>What one preview turned out to be of, and for whom.</summary>
public sealed record ContentScanUpdate(
    string ThumbnailName,
    IReadOnlyList<int> AssetIds,
    ContentEmbedding Vector,
    DateTime IndexedUtc);

/// <summary>One photograph's vector, as the search ranks against.</summary>
public readonly record struct ContentVector(int AssetId, ContentEmbedding Vector);
