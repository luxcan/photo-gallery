using System.Reflection;

namespace PhotoGallery.Application;

/// <summary>
/// What this build of the app calls itself.
/// </summary>
/// <remarks>
/// One place, because three things ask and they must agree: the About screen
/// says it to a person, the sharing payload carries it to another machine, and
/// the release check compares it against what has been published. Two of those
/// were already reading the assembly and the third was about to.
///
/// <para>The build stamps <c>1.0.0+&lt;commit&gt;</c> into the assembly - the
/// number from the project file and the commit from source control - so the
/// installed executable names both what it is and what it was built from. Only
/// the number in front is a version anybody can compare; the commit is there for
/// the person reading a log six months later.</para>
/// </remarks>
public static class AppVersion
{
    /// <summary>
    /// The whole of it, commit and all, exactly as the assembly carries it.
    /// </summary>
    public static string Full { get; } =
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "1.0.0";

    /// <summary>
    /// Just the number, for saying out loud and for comparing.
    /// </summary>
    public static string Number { get; } = Full.Split('+')[0];

    /// <summary>
    /// The same number as something that can be compared, or null where the
    /// assembly carries something this cannot make sense of.
    /// </summary>
    /// <remarks>
    /// Null rather than a guess. A build whose own version cannot be read has no
    /// business deciding that some other version is newer than it, and the one
    /// thing that reads this treats not knowing as nothing to report.
    /// </remarks>
    public static Version? Comparable { get; } =
        Version.TryParse(Number, out Version? parsed) ? parsed : null;
}
