using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Gives a folder that has only just been added the identity another computer
/// already uses for the same folder.
/// </summary>
/// <remarks>
/// The answer to "is this the same folder?" asked when a folder is added.
/// <see cref="ConfirmPairingHandler"/> links the two identities and settles both
/// on the lower one - right for two folders that have each been filing photos,
/// but the lower one is this folder's new id half the time, and then the other
/// computer's pictures and faces stay filed under its own id until it next
/// syncs. The first scan would take nothing, which is the one thing asking at
/// this moment is for.
///
/// <para>A folder with no photographs has nothing filed under its id: no
/// photograph, no answer, nothing published. So it simply takes theirs, and is
/// the same folder from the first scan on, with no link for anybody to
/// follow. Refused for a folder that has photographs, which must be paired
/// instead.</para>
/// </remarks>
public sealed class AdoptSameFolderHandler
{
    private readonly ILibraryIndex _index;
    private readonly IAssetRepository _assets;
    private readonly IDecisionRepository _repository;

    public AdoptSameFolderHandler(
        ILibraryIndex index, IAssetRepository assets, IDecisionRepository repository)
    {
        _index = index;
        _assets = assets;
        _repository = repository;
    }

    /// <param name="mine">The shared identity of the folder just added.</param>
    /// <param name="theirs">The identity another computer uses for the same folder.</param>
    /// <exception cref="InvalidOperationException">
    /// The folder is not this library's, already has photographs, or another of
    /// this library's folders already has that identity.
    /// </exception>
    public async Task HandleAsync(Guid mine, Guid theirs, CancellationToken cancellationToken = default)
    {
        if (theirs == Guid.Empty)
        {
            throw new ArgumentException("A folder cannot take an empty identity.", nameof(theirs));
        }

        IReadOnlyList<PhotoSource> sources =
            await _index.GetSourcesAsync(cancellationToken).ConfigureAwait(false);

        PhotoSource source = sources.SingleOrDefault(candidate => candidate.SharedId == mine)
            ?? throw new InvalidOperationException("That folder is not in this library.");

        if (mine == theirs)
        {
            return;
        }

        if (sources.Any(other => other.Id != source.Id && other.SharedId == theirs))
        {
            throw new InvalidOperationException(
                "Another folder in this library already is that folder, so it cannot be taken twice.");
        }

        if (await _assets.CountAsync(source.Id, cancellationToken).ConfigureAwait(false) > 0)
        {
            throw new InvalidOperationException(
                $"{source.Path} already has photos, so it has to be paired on the Sync screen.");
        }

        // The same rename a pairing applies, so anything already waiting here
        // under either identity ends up under the one the folder now has.
        MergePlan plan = MergePlan.Nothing with
        {
            Renames = new Dictionary<Guid, Guid> { [mine] = theirs },
        };

        await _repository.ApplyAsync(plan, null, cancellationToken).ConfigureAwait(false);
    }
}
