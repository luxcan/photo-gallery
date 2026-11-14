using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Application.Ports;

/// <summary>
/// The measurements the keeper ladder reads (PRP 14): eyes and sharpness per
/// face, sharpness and exposure per preview.
/// </summary>
/// <remarks>
/// Local to this library. Nothing here is published to the sync folder: every
/// measurement is read from pixels each computer already has, so each works it
/// out for itself, the way it works out its own duplicate sets.
/// </remarks>
public interface IPhotoQualityRepository
{
    /// <summary>
    /// The photographs among <paramref name="assetIds"/> that have a preview,
    /// with what is already measured on each.
    /// </summary>
    Task<IReadOnlyList<MeasuringCandidate>> GetCandidatesAsync(
        IReadOnlyCollection<int> assetIds, CancellationToken cancellationToken = default);

    /// <summary>Replaces whatever was measured on one photograph with this.</summary>
    Task SaveAsync(
        PhotoMeasurement measurement, DateTime measuredUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Each photograph's measurements as the ladder reads them, with only its
    /// known people's faces.
    /// </summary>
    /// <remarks>
    /// Known means confirmed as a named person, or matched by the app to one,
    /// on a face that is not ignored and a person not deleted. A stranger, a
    /// face nobody has named and a rejected or cleared match are left out here,
    /// so they cannot decide anything. A photograph never measured still comes
    /// back, with its size, length and details and no readings.
    /// </remarks>
    Task<IReadOnlyDictionary<int, CopyQuality>> GetQualitiesAsync(
        IReadOnlyCollection<int> assetIds, CancellationToken cancellationToken = default);
}
