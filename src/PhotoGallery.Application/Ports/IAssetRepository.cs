using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Application.Ports;

/// <summary>Reads and writes the indexed assets of a photo source.</summary>
public interface IAssetRepository
{
    /// <summary>
    /// Every indexed file of one source, keyed by relative path, carrying only
    /// what is needed to spot a change.
    /// </summary>
    /// <remarks>
    /// Loaded in one query rather than asking per file: 17,000 round trips to
    /// SQLite would cost far more than the dictionary does.
    /// </remarks>
    Task<Dictionary<string, AssetSignature>> GetSignaturesAsync(
        int photoSourceId, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyList<Asset> assets, CancellationToken cancellationToken = default);

    Task UpdateRangeAsync(IReadOnlyList<Asset> assets, CancellationToken cancellationToken = default);

    /// <summary>Removes assets whose files are gone from the source.</summary>
    Task RemoveAsync(IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default);

    Task<int> CountAsync(int photoSourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indexed file count per source, in one grouped query rather than one
    /// query per row.
    /// </summary>
    Task<Dictionary<int, int>> GetCountsBySourceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fills in creation dates for rows indexed before the app recorded them.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="UpdateRangeAsync"/> because those rows have not
    /// changed - the app simply did not know this fact when it first saw them.
    /// Putting them through the update path would clear every thumbnail, capture
    /// date and hash the library has built.
    /// </remarks>
    Task SetCreatedDatesAsync(
        IReadOnlyList<(int AssetId, DateTime CreatedUtc)> dates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records thumbnails, dimensions and perceptual hashes in one batch, and
    /// marks each of those assets ready.
    /// </summary>
    Task UpdateThumbnailsAsync(
        IReadOnlyList<ThumbnailUpdate> updates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records where photographs were taken, and that the question has been
    /// asked.
    /// </summary>
    /// <remarks>
    /// Deliberately not folded into <see cref="UpdateThumbnailsAsync"/>, which
    /// also writes dimensions, hashes, capture dates and readiness. The locating
    /// pass read none of those, and a write that set them would be claiming
    /// knowledge it does not have.
    /// </remarks>
    Task RecordLocationsAsync(
        IReadOnlyList<PhotoLocation> located, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a batch of videos' frames, their length and their size, and marks
    /// each of those clips ready.
    /// </summary>
    /// <remarks>
    /// Replaces whatever frames a video had rather than adding to them. Frames
    /// are only taken again once the file's length or modified time has changed,
    /// and the ones recorded from the previous file describe a clip that is no
    /// longer there.
    ///
    /// <para>The poster's name becomes the row's thumbnail, so everything that
    /// draws pictures finds a video exactly the way it finds a photograph.</para>
    /// </remarks>
    Task UpdateVideoKeyframesAsync(
        IReadOnlyList<VideoKeyframeUpdate> updates, CancellationToken cancellationToken = default);

    /// <summary>Records that these files could not be read or decoded.</summary>
    /// <remarks>
    /// Kept as a fact so the pass stops offering them. Without it a handful of
    /// broken files are opened again on every run, and the count of outstanding
    /// work never reaches zero.
    /// </remarks>
    Task MarkFailedAsync(IReadOnlyList<int> assetIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every row drawing one rendition, with the file each of them came from.
    /// </summary>
    /// <remarks>
    /// Every row, not one, because a rendition is named after the picture's
    /// content and duplicates therefore share a file - 245 in this library. A
    /// turn happens to that one file, so a row left behind would draw the same
    /// picture while disagreeing about which way up it is.
    /// </remarks>
    Task<IReadOnlyList<AssetFile>> FindSharingAsync(
        string thumbnailName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything needed to ask whether one picture should be deleted, and to
    /// do it: where the file is, which rendition it draws, and what would be
    /// forgotten along with it.
    /// </summary>
    /// <remarks>
    /// Read before the question is put, so the confirmation can name what is at
    /// stake rather than asking about "this photo" in the abstract. Null when
    /// the row has gone.
    /// </remarks>
    /// <summary>
    /// Everything a detail panel shows about one picture, or null when the row
    /// has gone.
    /// </summary>
    Task<PhotoFacts?> FindFactsAsync(
        int assetId, CancellationToken cancellationToken = default);

    Task<AssetToRemove?> FindForRemovalAsync(
        int assetId, CancellationToken cancellationToken = default);

    /// <summary>Adds a turn to what these rows already carry.</summary>
    Task TurnAsync(
        IReadOnlyList<int> assetIds, int degrees, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets these rows' turn outright, rather than adding to it.
    /// </summary>
    /// <remarks>
    /// Used to clear it back to none once the file itself has been told which
    /// way up it goes. The app's override exists only for pictures whose file
    /// cannot say, so a file that now can should not be second-guessed by it.
    /// </remarks>
    Task SetRotationAsync(
        IReadOnlyList<int> assetIds, int rotation, CancellationToken cancellationToken = default);

    /// <summary>How many assets already have a thumbnail recorded.</summary>
    Task<int> CountWithThumbnailsAsync(CancellationToken cancellationToken = default);

    /// <summary>Which of these rendition names any row still claims.</summary>
    /// <remarks>
    /// Asked after the rows that used to claim them have been rewritten, so
    /// whatever comes back genuinely belongs to something else. Renditions are
    /// shared - two identical pictures point at one pair of files - so a name may
    /// only be deleted once nothing at all refers to it.
    /// </remarks>
    Task<HashSet<string>> GetReferencedThumbnailNamesAsync(
        IReadOnlyCollection<string> names, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every indexed file of one source paired with the cached copy it claims,
    /// oldest row first.
    /// </summary>
    /// <remarks>
    /// Detaching walks these one at a time, deleting a record's files before its
    /// row. Stable order matters because a detach that is stopped part way is
    /// resumed by running it again.
    /// </remarks>
    Task<IReadOnlyList<AssetRendition>> ListRenditionsAsync(
        int photoSourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The distinct thumbnail names every source other than this one still uses.
    /// </summary>
    /// <remarks>
    /// Names are shared: two sources holding the same picture point at the same
    /// pair of files, so detaching may only delete what nothing else claims.
    /// Asking before anything has been removed means the answer does not depend
    /// on how far the detach has got.
    /// </remarks>
    Task<HashSet<string>> GetThumbnailNamesExceptAsync(
        int photoSourceId, CancellationToken cancellationToken = default);
}
