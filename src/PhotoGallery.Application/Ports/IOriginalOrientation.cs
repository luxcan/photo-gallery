namespace PhotoGallery.Application.Ports;

/// <summary>
/// Records a turn in the photograph's own file, so every other program sees it
/// the right way up too.
/// </summary>
/// <remarks>
/// Only the orientation tag is touched, and only where it already exists - two
/// bytes inside an entry that is already in the file's tag table, so no pixel is
/// decoded and nothing is re-encoded. Adding a tag that is absent would mean
/// growing the header and shifting the whole file, which this deliberately will
/// not do; those pictures are corrected in the app's own copies instead.
///
/// <para>Measured on a library of 10,529 JPEGs: of 150 sampled, every one of the
/// 132 that already carried a tag accepted the write and none of the 18 without
/// one did. The split is structural rather than statistical.</para>
/// </remarks>
public interface IOriginalOrientation
{
    /// <summary>
    /// Turns the file clockwise by recording it, leaving its timestamps as they
    /// were.
    /// </summary>
    /// <returns>
    /// True when the file now says which way up it goes. False when it could not
    /// be told - no tag to update, a mirrored orientation this will not reason
    /// about, a format without one, or the file being unavailable - in which case
    /// nothing about it was changed.
    /// </returns>
    bool TryTurn(string fullPath, int degrees);
}
