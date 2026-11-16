using PhotoGallery.Application.Ports;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// Takes this computer's own files back out of a sync folder.
/// </summary>
/// <remarks>
/// Each file goes by its full name, built from this computer's identity by the
/// same code that named it when it was written. Nothing is matched by pattern
/// and no folder is listed, so another computer's file cannot be taken however
/// the folder has been filled - and the folders themselves stay, since the
/// other computers still write into them.
///
/// <para>A temporary left by a write that was interrupted goes too: it holds
/// the same things as the file it was about to become.</para>
/// </remarks>
public sealed class SharedFolderCleanup : ISyncFolderCleanup
{
    private const string TempExtension = ".tmp";

    public Task<int> RemoveOwnFilesAsync(
        string folder, Guid machineId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (machineId == Guid.Empty)
        {
            throw new ArgumentException("This library has no identity to look for.", nameof(machineId));
        }

        // A folder on a drive that is switched off or out of reach answers "no
        // such file" for every one of this computer's files, which would read as
        // there being nothing left to remove while all of it is still there. That
        // is the likeliest moment to be leaving a folder, so it is said as a
        // failure - the person can try again once the folder is back - and asked
        // once here rather than waited on for each file.
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                "That folder cannot be reached at the moment, so this computer's files are still in it.");
        }

        string[] own =
        [
            Path.Combine(folder, SharedFolderExchange.AnswersFolder, SharedFolderExchange.NameOf(machineId)),
            Path.Combine(folder, SharedFolderPool.FactsFolder, SharedFolderPool.FactsNameOf(machineId)),
            Path.Combine(folder, SharedFolderPool.VectorsFolder, SharedFolderPool.VectorsNameOf(machineId)),
        ];

        int removed = 0;

        foreach (string file in own.SelectMany(path => new[] { path, path + TempExtension }))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(file))
            {
                File.Delete(file);
                removed++;
            }
        }

        return Task.FromResult(removed);
    }
}
