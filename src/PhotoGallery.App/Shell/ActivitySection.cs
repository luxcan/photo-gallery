namespace PhotoGallery.App.Shell;

/// <summary>
/// One mode in the side nav. The glyph is a Segoe MDL2 Assets code point, which
/// ships with Windows and needs no icon assets in the bundle.
/// </summary>
/// <param name="RequiresSources">
/// True for sections that have nothing to show until photos are added, so they
/// stay disabled rather than opening onto an empty screen.
/// </param>
public sealed record ActivitySection(
    string Key,
    string Title,
    string Glyph,
    bool RequiresSources)
{
    // Public, and here rather than private to the view model, because the nav's
    // count converter has to name the same sections - and two copies of six
    // strings is one copy too many.
    public const string LibraryKey = "library";

    public const string PeopleKey = "people";

    public const string DuplicatesKey = "duplicates";

    public const string SourcesKey = "sources";

    public const string AboutKey = "about";

    public const string SettingsKey = "settings";
}
