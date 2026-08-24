using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sources;

namespace PhotoGallery.Application.UseCases.Gallery;

/// <summary>
/// Deletes one photograph: the file, the copies made of it, and everything the
/// app worked out about it.
/// </summary>
/// <remarks>
/// The only thing in this app that destroys something the user cannot get back,
/// so three rules govern it.
///
/// <para>The source is asked first. Everything below turns on being able to tell
/// a file that has gone from a file nobody can currently see, and only the
/// source's root can answer that - <see cref="ISourceAvailability"/> explains
/// why. A source that cannot be reached ends the matter for every photograph on
/// it: nothing is read, nothing is deleted, nothing is forgotten.</para>
///
/// <para>The file goes second. If it will not go, nothing else does either - a
/// library that had forgotten a photograph still sitting on disk would simply
/// re-index it on the next scan and quietly resurrect it without its
/// names.</para>
///
/// <para>The cached pictures are only removed when no other row still draws
/// them. Renditions are named after the picture's content, so duplicates share
/// one pair of files, and deleting them for this row would leave the others
/// blank.</para>
/// </remarks>
public sealed class RemovePhotoHandler
{
    private readonly IAssetRepository _assets;
    private readonly IOriginalFile _original;
    private readonly IThumbnailStore _thumbnails;
    private readonly ISourceAvailability _availability;

    public RemovePhotoHandler(
        IAssetRepository assets,
        IOriginalFile original,
        IThumbnailStore thumbnails,
        ISourceAvailability availability)
    {
        _assets = assets;
        _original = original;
        _thumbnails = thumbnails;
        _availability = availability;
    }

    /// <summary>
    /// Which of these photographs' sources cannot be reached, named once each.
    /// </summary>
    /// <remarks>
    /// For asking before the question is put, so an unreachable share is met
    /// with "nothing was changed" rather than with a confirmation that is
    /// granted and then quietly does nothing.
    ///
    /// <para>Synchronous, like the deleting it guards, and slow for the same
    /// reason: it goes to the share. Callers run it off the dispatcher.</para>
    /// </remarks>
    public IReadOnlyList<string> UnreachableSources(IReadOnlyList<PhotoToRemove> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);

        return new ReachableSources(_availability)
            .OutOfReach(photos.Select(photo => photo.SourceRoot));
    }

    /// <summary>
    /// What deleting this picture would cost, for the question to be put with.
    /// </summary>
    public async Task<PhotoToRemove?> DescribeAsync(
        int assetId, CancellationToken cancellationToken = default)
    {
        AssetToRemove? asset = await _assets
            .FindForRemovalAsync(assetId, cancellationToken)
            .ConfigureAwait(false);

        return asset is null
            ? null
            : new PhotoToRemove(
                asset.AssetId,
                asset.FileName,
                asset.FullPath,
                asset.SourceRoot,
                asset.Faces,
                asset.Names,
                _original.GoesToRecycleBin(asset.FullPath));
    }

    /// <summary>
    /// Deletes photographs, naming each one before it goes.
    /// </summary>
    /// <remarks>
    /// One entry point for every way a picture can leave this library - the
    /// viewer, the face review and both of the duplicates screen's buttons - so
    /// the progress the user watches is the same whichever gesture started it,
    /// and so a fifth way to delete cannot be added without it.
    ///
    /// <para>The token is read only between photographs. A deletion is a file, a
    /// rendition and a row, and stopping part way through one would leave a row
    /// for a file that is no longer there; each photograph is therefore
    /// indivisible and stopping means stopping before the next one. That is why
    /// the steps below pass <see cref="CancellationToken.None"/> - the work has
    /// been done, and a row that failed to record it would have the next scan
    /// resurrect the photograph without its names.</para>
    ///
    /// <para>A file that will not go does not stop the rest. It is named in the
    /// result instead, with its row and cached copies untouched.</para>
    ///
    /// <para>Every photograph's source is checked here as well as before the
    /// question was put, and the check is per photograph rather than once at the
    /// top. A batch of four hundred duplicates takes minutes, a share can drop
    /// during it, and the moment it does every remaining file starts reporting
    /// itself as already gone. Asked once, that is a library that deletes the
    /// rest of the batch off the back of a dropped connection.</para>
    /// </remarks>
    public async Task<PhotoRemovalResult> HandleAsync(
        IReadOnlyList<int> assetIds,
        IProgress<PhotoRemovalProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        var reachable = new ReachableSources(_availability);
        var refused = new List<int>();
        var outOfReach = new List<int>();
        var unreachableSources = new List<string>();
        int deleted = 0;

        for (int at = 0; at < assetIds.Count; at++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new PhotoRemovalResult(
                    deleted, refused, true, outOfReach, unreachableSources);
            }

            int assetId = assetIds[at];
            AssetToRemove? asset = await _assets
                .FindForRemovalAsync(assetId, CancellationToken.None)
                .ConfigureAwait(false);

            if (asset is null)
            {
                // Already gone from the index. Counted as refused rather than
                // deleted, because a group is only finished when every copy in
                // it was actually dealt with by this pass.
                refused.Add(assetId);
                continue;
            }

            progress?.Report(new PhotoRemovalProgress(
                at, assetIds.Count, asset.FileName, asset.ThumbnailName));

            if (!reachable.CanReach(asset.SourceRoot))
            {
                // Nothing is known about this photograph, so nothing is done to
                // it. Not counted as refused: a refusal says the file is there
                // and would not go, and that is a claim nobody is in a position
                // to make.
                outOfReach.Add(assetId);

                if (!unreachableSources.Contains(asset.SourceRoot, StringComparer.OrdinalIgnoreCase))
                {
                    unreachableSources.Add(asset.SourceRoot);
                }

                continue;
            }

            if (await RemoveAsync(asset).ConfigureAwait(false))
            {
                deleted++;
            }
            else
            {
                refused.Add(assetId);
            }
        }

        // A last report so the bar finishes full and the picture clears, rather
        // than the screen ending on the one photograph that happened to be last.
        progress?.Report(new PhotoRemovalProgress(
            assetIds.Count, assetIds.Count, string.Empty, null));

        return new PhotoRemovalResult(deleted, refused, false, outOfReach, unreachableSources);
    }

    /// <summary>
    /// The three steps that take one photograph, in the only order that is safe
    /// to be interrupted at.
    /// </summary>
    private async Task<bool> RemoveAsync(AssetToRemove asset)
    {
        if (!_original.Delete(asset.FullPath))
        {
            return false;
        }

        if (asset.OtherCopies == 0)
        {
            // Best effort. A rendition left behind is a few hundred kilobytes
            // that the next sweep of the working folder collects; refusing to
            // forget the photograph over it would be far worse.
            _thumbnails.TryDelete(asset.ThumbnailName);
        }

        // The faces and everything said about them go with the row: the index
        // cascades from the asset, so this is one delete rather than four in an
        // order that could be interrupted halfway.
        await _assets.RemoveAsync([asset.AssetId], CancellationToken.None).ConfigureAwait(false);

        return true;
    }
}

/// <summary>What is about to be lost, so the user can be asked properly.</summary>
/// <param name="Recoverable">
/// Whether the file would go to the Recycle Bin. False on a network share or a
/// removable drive, where Windows deletes outright - and the question has to say
/// so rather than implying an undo that does not exist.
/// </param>
/// <param name="SourceRoot">
/// The root that has to be reachable before this photograph may be touched.
/// </param>
public sealed record PhotoToRemove(
    int AssetId,
    string FileName,
    string FullPath,
    string SourceRoot,
    int Faces,
    int Names,
    bool Recoverable);
