namespace PhotoGallery.Application.Ports;

/// <summary>
/// Takes this computer's own files back out of a folder it no longer syncs
/// through.
/// </summary>
/// <remarks>
/// What a computer leaves in the sync folder is everything another computer
/// needs to catch up with it - the names given to people and their birth years,
/// what each face looks like to the app, where and when each photograph was
/// taken - in plain files that anybody who can open the folder can read. Choosing
/// another folder used to leave all of it behind in the old one for good.
///
/// <para><strong>Only the files named after this computer.</strong> Every other
/// computer's are left exactly as they are, and so are the small copies of
/// photographs: those are named after the photograph rather than after
/// whoever put them there, two computers with the same photograph leave the
/// same file, and nothing in the folder can say which of them a copy is
/// still needed by.</para>
/// </remarks>
public interface ISyncFolderCleanup
{
    /// <summary>
    /// Removes the files this computer wrote into <paramref name="folder"/>, and
    /// answers how many there were.
    /// </summary>
    Task<int> RemoveOwnFilesAsync(
        string folder, Guid machineId, CancellationToken cancellationToken = default);
}
