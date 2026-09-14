using Microsoft.EntityFrameworkCore;

namespace PhotoGallery.App.Shell;

/// <summary>
/// The rows a failed write could not account for, for the log.
/// </summary>
/// <remarks>
/// A write that fails says which operation and which method, and the stack says
/// the rest - but it does not say which row, and that is the one thing needed to
/// work out how a row came to be missing. The exception already carries it:
/// <see cref="DbUpdateException.Entries"/> holds the entities the save could not
/// write. Printing them turns "some row was gone" into "the membership for asset
/// 40,113 was gone", which is the difference between a theory and a fix.
///
/// <para>Keys only, and every key in this library is a number. Nothing here can
/// put a file name, a folder or a person's name into a log file somebody might
/// send on.</para>
/// </remarks>
internal static class WriteFault
{
    /// <summary>
    /// One line per row the write could not account for, or empty when the
    /// failure was not a write.
    /// </summary>
    internal static string Rows(Exception failure)
    {
        if (failure is not DbUpdateException write || write.Entries.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            write.Entries.Select(entry =>
                $"    {entry.Metadata.DisplayName()} {entry.State}: "
                + string.Join(
                    ", ",
                    entry.Properties
                        .Where(property => property.Metadata.IsPrimaryKey())
                        .Select(property => $"{property.Metadata.Name}={property.CurrentValue}"))));
    }
}
