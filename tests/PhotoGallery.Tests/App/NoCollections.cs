using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests.App;

/// <summary>A library with no shelves, for the tests that are about albums.</summary>
/// <remarks>
/// The albums screen reads the collections whenever it reloads, because a band
/// with counts on it goes stale the moment an album changes. Tests that are
/// about the albums themselves still have to answer that read, and the honest
/// answer for them is "there are none".
///
/// <para>Shared rather than nested in each of them. It is already needed twice,
/// and the doubles beside it that were written per file are the reason this one
/// was not.</para>
/// </remarks>
internal sealed class NoCollections : ICollectionRepository
{
    public Task<IReadOnlyList<CollectionSummary>> GetAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CollectionSummary>>([]);

    public Task<int> CreateAsync(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task RenameAsync(
        int collectionId, string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteAsync(int collectionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CollectionFillResult> SetAlbumsAsync(
        int collectionId,
        IReadOnlyList<int> albumIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
