namespace PhotoGallery.Application.Ports;

/// <summary>
/// The user's own photograph on disk, which this app otherwise only ever reads.
/// </summary>
public interface IOriginalFile
{
    /// <summary>
    /// Whether deleting this file would put it in the Recycle Bin.
    /// </summary>
    /// <remarks>
    /// Asked before the question is put to the user, not after. Windows only
    /// keeps a Recycle Bin for local fixed drives: a file on a network share or
    /// a removable disk is deleted outright, however it was asked for. A library
    /// living on a share - as the measured one does - therefore has no undo at
    /// all, and telling somebody their photograph can be recovered when it
    /// cannot is the worst thing this feature could do.
    /// </remarks>
    bool GoesToRecycleBin(string fullPath);

    /// <summary>
    /// Deletes the file, preferring the Recycle Bin where there is one.
    /// </summary>
    /// <returns>
    /// True when the file is gone - including when it was already gone, since
    /// the library should not keep a row for a photograph that is not there.
    /// False when it is still on disk, in which case nothing else may be
    /// forgotten about it either.
    /// </returns>
    bool Delete(string fullPath);
}
