using System.IO;
using Microsoft.VisualBasic.FileIO;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Storage;

/// <inheritdoc cref="IOriginalFile"/>
public sealed class WindowsOriginalFile : IOriginalFile
{
    public bool GoesToRecycleBin(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        // A share has no Recycle Bin of its own and Windows will not use the
        // client's, so this is settled before the drive is even looked at.
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(fullPath));
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            // Fixed disks only. Removable and network drives delete outright,
            // and an optical or RAM disk has nowhere to put anything either.
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException
                                      or UnauthorizedAccessException
                                      or NotSupportedException)
        {
            // Unable to tell, so claim nothing. The question the user is asked
            // then says the deletion cannot be undone, which is the safe way to
            // be wrong.
            return false;
        }
    }

    public bool Delete(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        try
        {
            if (!File.Exists(fullPath))
            {
                // Already gone. Keeping a row for a photograph that is not there
                // only means offering the user a picture that cannot open.
                //
                // True only because the caller has already established that this
                // file's source can be reached. File.Exists cannot tell absence
                // from an absent share - it answers false for both - so read on
                // its own this line forgets photographs that are perfectly safe
                // on a NAS that happens to be off. RemovePhotoHandler asks the
                // source's root first, and that is what makes this sound.
                return true;
            }

            // SendToRecycleBin rather than DeletePermanently: on a drive that
            // has one this is the difference between a mistake and a loss, and
            // on a drive that has none Windows deletes outright regardless - the
            // case the confirmation has already been explicit about.
            FileSystem.DeleteFile(
                fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

            return !File.Exists(fullPath);
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or OperationCanceledException)
        {
            // A file open elsewhere, read-only, or on a share that has gone
            // away. Reported as a failure so the row and the renditions are
            // left alone and the library still matches the disk.
            return false;
        }
    }
}
