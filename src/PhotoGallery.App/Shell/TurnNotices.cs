namespace PhotoGallery.App.Shell;

/// <summary>
/// What the badge beside the turn buttons can say.
/// </summary>
/// <remarks>
/// Held in one place because both the gallery viewer and the face review show
/// the same badge, and the wording was previously copied into each of their
/// XAML blocks - where it drifted out of step with what the code behind it
/// actually meant.
///
/// <para>The badge describes the <em>photograph</em>, not the last thing that
/// happened to it. A turn that was refused because its folder is away is an
/// event, and gets a dialog from the shell instead; a badge would still be
/// sitting there long after the share came back.</para>
/// </remarks>
internal static class TurnNotices
{
    /// <summary>The file was asked, and it cannot hold the answer.</summary>
    public const string HereOnly = "Turned here only";

    public const string HereOnlyTip =
        "This file cannot record which way up it goes, so it will still look "
        + "wrong in Explorer and other apps.";
}
