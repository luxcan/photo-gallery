using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Gallery;

/// <summary>Reads the pictures the gallery is currently showing.</summary>
public sealed class QueryGalleryHandler
{
    private readonly IGalleryReader _reader;

    public QueryGalleryHandler(IGalleryReader reader) => _reader = reader;

    public Task<GalleryPage> HandleAsync(
        GalleryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A folder name without its source is ambiguous - two sources may hold
        // folders of the same name - and the range predicate would silently
        // merge them rather than fail.
        if (!string.IsNullOrWhiteSpace(query.FolderPath) && query.PhotoSourceId is null)
        {
            throw new ArgumentException(
                "A folder must be asked for together with the source it belongs to.",
                nameof(query));
        }

        return _reader.QueryAsync(query, cancellationToken);
    }
}
