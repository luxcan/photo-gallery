using PhotoGallery.Domain.Search;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// Turns a photograph, or a description of one, into a point in the same space.
/// </summary>
/// <remarks>
/// One port for both halves because they are one model: the encoders were
/// trained as a pair and their answers are only comparable to each other. Split
/// across two ports it would be possible to configure a text encoder from one
/// release against a visual encoder from another, and the result of that is not
/// an error - it is a search that returns confident nonsense.
/// </remarks>
public interface IContentEncoder
{
    /// <summary>
    /// What a photograph is of, read from its cached preview.
    /// </summary>
    /// <returns>Null when the preview cannot be read, which is not the same as
    /// a picture of nothing.</returns>
    Task<ContentEmbedding?> DescribePictureAsync(
        string previewPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a typed phrase is asking for.
    /// </summary>
    /// <returns>Null when there is nothing to encode.</returns>
    Task<ContentEmbedding?> DescribePhraseAsync(
        string phrase, CancellationToken cancellationToken = default);
}
