using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sources;

namespace PhotoGallery.Application.UseCases.Gallery;

/// <summary>
/// Turns one photograph the right way up, and keeps everything known about it
/// pointing at the same places.
/// </summary>
/// <remarks>
/// EXIF orientation is already applied when a rendition is built, so this is for
/// pictures whose file does not say which way is up: a phone held upside down
/// that wrote no tag, or one that wrote the wrong one. Only the person looking
/// at it can tell.
///
/// <para>Three things move together and none of them may move alone - the cached
/// pictures, the boxes drawn on them, and what is recorded against the row. The
/// order matters: nothing is recorded until the pictures have actually turned,
/// so a rendition that could not be read leaves the library exactly as it
/// was.</para>
///
/// <para>Where the file can be told which way up it goes, it is - and the app's
/// own override is then cleared, because a file that describes itself should not
/// be second-guessed. Where it cannot, the override is what keeps the picture
/// upright, and the row saying so is what lets the app admit that the original
/// is still wrong everywhere else.</para>
///
/// <para>All of which rests on having been able to ask the file, so the source
/// is checked before anything moves. A share that is away cannot be asked, and
/// turning the cached copies anyway would record the one answer this handler
/// must never give by default: that the file cannot hold the tag. It can, very
/// probably - nobody was there to try. The picture is left alone instead.</para>
/// </remarks>
public sealed class TurnPhotoHandler
{
    private readonly IRenditionTurner _renditions;
    private readonly IOriginalOrientation _originals;
    private readonly IAssetRepository _assets;
    private readonly IFaceRepository _faces;
    private readonly ISourceAvailability _availability;

    public TurnPhotoHandler(
        IRenditionTurner renditions,
        IOriginalOrientation originals,
        IAssetRepository assets,
        IFaceRepository faces,
        ISourceAvailability availability)
    {
        _renditions = renditions;
        _originals = originals;
        _assets = assets;
        _faces = faces;
        _availability = availability;
    }

    public async Task<TurnedPhoto> HandleAsync(
        string? thumbnailName, int degrees, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(thumbnailName) || ((degrees % 360) + 360) % 360 == 0)
        {
            return TurnedPhoto.Nothing;
        }

        // Read before the renditions are touched rather than after, which is the
        // order this used to run in. The rows are what name the sources, and the
        // sources are what decide whether any of this may happen at all - turning
        // the cached copies first would mean deciding to stop after having
        // already changed something.
        IReadOnlyList<AssetFile> sharing = await _assets
            .FindSharingAsync(thumbnailName, cancellationToken)
            .ConfigureAwait(false);

        if (sharing.Count == 0)
        {
            return TurnedPhoto.Nothing;
        }

        // Each row separately: two rows can share a rendition and sit on
        // different sources, and one of those being away is enough to stop this.
        // Turning a picture that half its rows cannot record would leave those
        // two disagreeing about which way up it is.
        IReadOnlyList<string> outOfReach = new ReachableSources(_availability)
            .OutOfReach(sharing.Select(file => file.SourceRoot));

        if (outOfReach.Count > 0)
        {
            return TurnedPhoto.OutOfReach(outOfReach);
        }

        if (_renditions.Turn(thumbnailName, degrees) is not TurnedRendition before)
        {
            return TurnedPhoto.Nothing;
        }

        // Each copy is its own file and each is asked separately: two rows can
        // share a picture and still be one JPEG that can hold the tag and one
        // that cannot.
        List<int> told = [];
        List<int> cachedOnly = [];

        foreach (AssetFile file in sharing)
        {
            if (_originals.TryTurn(file.FullPath, degrees))
            {
                told.Add(file.AssetId);
            }
            else
            {
                cachedOnly.Add(file.AssetId);
            }
        }

        await _faces
            .TurnFacesAsync(
                [.. sharing.Select(file => file.AssetId)],
                degrees,
                before.Width,
                before.Height,
                cancellationToken)
            .ConfigureAwait(false);

        await _assets.SetRotationAsync(told, 0, cancellationToken).ConfigureAwait(false);
        await _assets.TurnAsync(cachedOnly, degrees, cancellationToken).ConfigureAwait(false);

        return new TurnedPhoto(true, told.Count, cachedOnly.Count, []);
    }
}

/// <summary>What a turn managed to change.</summary>
/// <param name="OriginalsTold">
/// How many of the files themselves now say which way up they go, and so look
/// right in every other program too.
/// </param>
/// <param name="CachedOnly">
/// How many could only be corrected in this app's copies, because their file has
/// no orientation tag and no room to add one.
/// </param>
/// <param name="UnreachableSources">
/// The sources that could not be reached, when that is why nothing happened.
/// Empty on every other outcome. Distinct from <paramref name="CachedOnly"/> on
/// purpose: that one means the file was asked and said no, this one means the
/// file was never asked.
/// </param>
public readonly record struct TurnedPhoto(
    bool Turned,
    int OriginalsTold,
    int CachedOnly,
    IReadOnlyList<string> UnreachableSources)
{
    public static TurnedPhoto Nothing => new(false, 0, 0, []);

    /// <summary>Nothing was turned, because these sources are away.</summary>
    public static TurnedPhoto OutOfReach(IReadOnlyList<string> sources) =>
        new(false, 0, 0, sources);
}
