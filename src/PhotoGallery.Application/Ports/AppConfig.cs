namespace PhotoGallery.Application.Ports;

/// <summary>
/// The one thing that must be known before any library is open: which one.
/// </summary>
/// <remarks>
/// Everything else - assets, people, duplicates, thumbnails, and the palette -
/// belongs to a library and lives in its working folder. The theme was kept here
/// as well while the set-up screen appeared on every launch, so it could open in
/// the right colours; now that the app goes straight to the remembered library,
/// the only window that can appear before a database is the set-up screen, and
/// that only appears when there is no library to have a preference. A second
/// copy that can never be the right answer is worse than none.
/// </remarks>
public sealed record AppConfig
{
    public string? LastWorkingFolder { get; init; }

    /// <summary>
    /// Whether to write a detailed log of what the app is doing.
    /// </summary>
    /// <remarks>
    /// Here rather than in the library's settings because the most useful thing
    /// it can record is a start-up that never reached a library at all. Off by
    /// default: it is for diagnosing a problem, not a thing to accumulate.
    /// </remarks>
    public bool Diagnostics { get; init; }

    /// <summary>
    /// Where the optional model files are kept, when the user has moved them.
    /// </summary>
    /// <remarks>
    /// Here rather than in a library's settings because the files belong to no
    /// library in particular: they are 1.9 GB, and opening a second library
    /// should not mean downloading them a second time. Null means the default,
    /// which is inside whichever library is open.
    /// </remarks>
    public string? ModelsFolder { get; init; }

    public static AppConfig Empty { get; } = new();
}
