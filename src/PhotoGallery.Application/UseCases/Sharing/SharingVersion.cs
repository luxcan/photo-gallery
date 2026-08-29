using System.Reflection;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>What this release can read, and what it calls itself.</summary>
public static class SharingVersion
{
    /// <summary>
    /// The shape decisions are written in.
    /// </summary>
    /// <remarks>
    /// Read before anything else in a payload. A machine running a newer release
    /// is refused whole rather than partly applied, because half a schema applied
    /// is a library that looks fine and disagrees with itself - and because the
    /// machine that cannot read it is in no position to guess which half was
    /// safe.
    ///
    /// <para>Raised only when the meaning of something already published
    /// changes. A field nobody older reads is not a new schema; a field whose
    /// absence would be understood as an answer is.</para>
    /// </remarks>
    public const int Schema = 1;

    /// <summary>
    /// What to call this release on the other machine's screen, and nothing
    /// more - no decision is ever made from it.
    /// </summary>
    public static string App { get; } =
        typeof(SharingVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "1.0.0";
}
