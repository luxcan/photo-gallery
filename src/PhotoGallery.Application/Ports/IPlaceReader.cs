namespace PhotoGallery.Application.Ports;

/// <summary>The read side of places: what the search box can offer.</summary>
public interface IPlaceReader
{
    /// <summary>
    /// Every place at least one photograph was taken, with its count, most
    /// photographed first.
    /// </summary>
    /// <remarks>
    /// Read whole rather than queried per keystroke, exactly as the people
    /// directory is: a library has as many places as it has been to, which is
    /// hundreds and not millions.
    /// </remarks>
    Task<IReadOnlyList<PlaceDirectoryEntry>> GetDirectoryAsync(
        CancellationToken cancellationToken = default);
}
