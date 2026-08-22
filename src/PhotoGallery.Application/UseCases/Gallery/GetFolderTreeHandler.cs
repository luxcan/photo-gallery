using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Gallery;

/// <summary>
/// The folder tree beside the grid.
/// </summary>
/// <remarks>
/// Runs once when the folder view opens rather than per page: the whole tree for
/// this library is 210 nodes built from 11,482 paths, which is 12 ms.
/// </remarks>
public sealed class GetFolderTreeHandler
{
    private readonly IGalleryReader _reader;

    public GetFolderTreeHandler(IGalleryReader reader) => _reader = reader;

    public Task<IReadOnlyList<FolderNode>> HandleAsync(
        CancellationToken cancellationToken = default) =>
        _reader.GetFoldersAsync(cancellationToken);
}
