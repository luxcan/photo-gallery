namespace PhotoGallery.Domain.Library;

/// <summary>Which palette the user wants, remembered between sessions.</summary>
public enum ThemePreference
{
    /// <summary>Follow Windows, and keep following it if the user changes it.</summary>
    System = 0,

    Light = 1,

    Dark = 2,
}
