using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sources;
using PhotoGallery.Domain.Library;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Takes this computer's own files out of a folder it has stopped syncing
/// through.
/// </summary>
/// <remarks>
/// Offered, never done on its own, when somebody chooses a different sync
/// folder or stops syncing: what this computer left there is readable by
/// anybody who can open that folder, and nothing else would ever take it away.
///
/// <para><strong>Never the folder in use.</strong> The next sync would only
/// write the same files again, and in between the other computers would see
/// this one as having left. The question is only ever about a folder being
/// left behind, so a folder that is still the sync folder is refused rather
/// than emptied.</para>
/// </remarks>
public sealed class RemoveOwnSyncFilesHandler
{
    private readonly ILibraryIndex _index;
    private readonly ISyncFolderCleanup _cleanup;

    public RemoveOwnSyncFilesHandler(ILibraryIndex index, ISyncFolderCleanup cleanup)
    {
        _index = index;
        _cleanup = cleanup;
    }

    /// <returns>How many of this computer's files were there to remove.</returns>
    public async Task<int> HandleAsync(string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        string left = FolderOverlap.Normalise(folder);

        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(settings.SharedFolder)
            && string.Equals(
                FolderOverlap.Normalise(settings.SharedFolder), left, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "That is the folder this computer syncs through now, so its files stay there.");
        }

        return await _cleanup
            .RemoveOwnFilesAsync(left, settings.MachineId, cancellationToken)
            .ConfigureAwait(false);
    }
}
