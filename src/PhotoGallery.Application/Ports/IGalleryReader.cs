namespace PhotoGallery.Application.Ports;

/// <summary>
/// The read side of the library: what the gallery shows, and what work is
/// outstanding.
/// </summary>
/// <remarks>
/// Separate from <see cref="IAssetRepository"/> because reading for display and
/// writing during a scan want different shapes - the gallery wants joined,
/// projected rows it can bind to, not tracked entities.
/// </remarks>
public interface IGalleryReader
{
    /// <summary>
    /// Every photo, with whatever thumbnail its row claims, so the pass can see
    /// which claims the disk actually backs.
    /// </summary>
    /// <remarks>
    /// It returns all of them rather than only those with no name because the
    /// name is not proof: 11,481 rows in the developer's own library named a
    /// tile that had been deleted, and a pass filtering on the column alone
    /// found one photo to do instead of eleven thousand. Deciding on the disk
    /// costs one existence check per row and cannot be wrong.
    /// </remarks>
    Task<IReadOnlyList<PendingThumbnail>> GetThumbnailCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Photos whose faces have never been looked for, newest first.
    /// </summary>
    /// <remarks>
    /// Filtered on the column rather than decided by the disk, which is the
    /// opposite of the thumbnail candidates above and is right for the opposite
    /// reason: there is no file whose presence answers the question. A photo
    /// with no faces in it produces nothing, so only the date can say whether it
    /// has been examined.
    /// </remarks>
    /// <summary>
    /// Every video that might still need its frames taken, with the facts their
    /// names are derived from.
    /// </summary>
    /// <remarks>
    /// Videos that already failed to decode are left out here rather than
    /// filtered afterwards, for the same reason photographs are: a container
    /// Windows has no codec for must not cost an open on every pass for the rest
    /// of time.
    /// </remarks>
    Task<IReadOnlyList<PendingVideo>> GetVideoCandidatesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FaceScanCandidate>> GetFaceCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Photographs whose rendition has not been described yet.
    /// </summary>
    /// <remarks>
    /// Having a row in the content index is the whole of the resumability
    /// marker, so this is the assets that have none. No date and no status of its
    /// own: unlike faces, where a picture with none in it looks exactly like one
    /// never examined, every photograph has one answer to what it is of.
    /// </remarks>
    Task<IReadOnlyList<ContentScanCandidate>> GetContentCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Photographs whose location has never been worked out, newest first.
    /// </summary>
    /// <remarks>
    /// Filtered on <c>LocationReadUtc</c> for the same reason the face candidates
    /// are filtered on their own date: five photographs in six carry no GPS, so a
    /// null latitude cannot say whether the file was asked. Selecting on the
    /// coordinates instead would re-read nine thousand originals over the share
    /// on every run, for ever.
    /// </remarks>
    Task<IReadOnlyList<LocationCandidate>> GetLocationCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Pictures for the grid, newest first.</summary>
    Task<GalleryPage> QueryAsync(
        GalleryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every folder that holds photos, per source, with counts rolled up and the
    /// source itself as the root.
    /// </summary>
    Task<IReadOnlyList<FolderNode>> GetFoldersAsync(
        CancellationToken cancellationToken = default);
}
