using System.IO;
using System.Runtime.InteropServices;
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

            // Asked again rather than trusted: this is the same answer the
            // confirmation was worded from, so it says which promise was made.
            ShellDelete(Path.GetFullPath(fullPath), FlagsFor(GoesToRecycleBin(fullPath)));

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

    /// <summary>
    /// How the shell is asked to delete, given what the user was told.
    /// </summary>
    /// <remarks>
    /// Always to the Recycle Bin where Windows can, and never with a progress
    /// window or a second "are you sure" - the app has already asked.
    ///
    /// <para>But "the file goes to the Recycle Bin" is a promise Windows cannot
    /// always keep on a fixed drive: a video bigger than the bin, a drive whose
    /// bin is switched off, a folder that is really a link to a share. Asked
    /// silently, Windows then destroys the file without a word. So when that
    /// promise was made, Windows is told to stop and ask before it deletes
    /// anything it cannot recycle; answered "No", the file stays and is reported
    /// as one that would not go. When the user was already told the deletion is
    /// permanent, asking again for every file of a batch would only be noise.</para>
    /// </remarks>
    internal static ushort FlagsFor(bool promisedRecycleBin)
    {
        ushort flags = FofSilent | FofNoConfirmation | FofAllowUndo;
        return promisedRecycleBin ? (ushort)(flags | FofWantNukeWarning) : flags;
    }

    private static void ShellDelete(string fullPath, ushort flags)
    {
        var operation = new ShellFileOperation
        {
            Function = FoDelete,

            // A list of paths, so it ends with an extra terminator.
            From = fullPath + '\0',
            Flags = flags,
        };

        // The result is not trusted either way: the caller asks the disk whether
        // the file is still there, which is the only answer that matters.
        _ = SHFileOperation(ref operation);
    }

    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    internal const ushort FofWantNukeWarning = 0x4000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShellFileOperation operation);

    /// <summary>The shell's SHFILEOPSTRUCTW, in its 64-bit layout.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public nint Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;
        public nint NameMappings;
        public string? ProgressTitle;
    }
}