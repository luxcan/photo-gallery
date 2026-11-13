using System.Diagnostics;
using System.Globalization;
using PhotoGallery.Infrastructure.Updates;

namespace PhotoGallery.App.Updates;

/// <summary>
/// The first moments of a version that has just replaced the one before it.
/// </summary>
/// <remarks>
/// The previous version starts this one and then closes. Until it has closed it
/// may still hold the library's database, and a migration run by this version
/// while the old one is mid-write is the one way an update could damage the
/// library. So this waits for it first.
/// </remarks>
internal static class AfterUpdate
{
    /// <summary>How long the previous version is given to close.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Waits for the version this one replaced, when it was started by one.
    /// </summary>
    /// <returns>Whether this start is the end of an update.</returns>
    public static bool WaitForPrevious(IReadOnlyList<string> args)
    {
        if (PreviousProcess(args) is not int id)
        {
            return false;
        }

        try
        {
            using Process previous = Process.GetProcessById(id);
            previous.WaitForExit(Patience);
        }
        catch (ArgumentException)
        {
            // Already gone, which is what was being waited for.
        }
        catch (InvalidOperationException)
        {
            // Exited between being found and being asked.
        }

        return true;
    }

    /// <summary>The process id after <c>--after-update</c>, or null.</summary>
    internal static int? PreviousProcess(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (int i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], SelfReplacingInstaller.AfterUpdateArgument, StringComparison.Ordinal)
                && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int id))
            {
                return id;
            }
        }

        return null;
    }
}
