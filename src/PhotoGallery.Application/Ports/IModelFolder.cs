namespace PhotoGallery.Application.Ports;

/// <summary>
/// Where the optional model files are kept, which the user can move.
/// </summary>
/// <remarks>
/// It starts inside the library's working folder, which is the right answer for
/// somebody who has one library and no opinion. It is not the right answer for
/// everybody: the files are 1.9 GB, they belong to no library in particular, and
/// a second library should not mean a second copy of them - so the folder is a
/// choice, remembered beside the library path rather than inside any library.
/// </remarks>
public interface IModelFolder
{
    /// <summary>The folder in use, chosen or not.</summary>
    string Path { get; }

    /// <summary>Where they live when nobody has chosen anything.</summary>
    string Default { get; }

    /// <summary>Whether the folder in use is one the user picked.</summary>
    bool WasChosen { get; }

    /// <summary>Moves the app's attention to another folder, and remembers it.</summary>
    /// <remarks>
    /// Nothing is copied or deleted. What was in the old folder stays there; if
    /// the new one already holds the files, the features come straight back on,
    /// and if it does not they read as not installed until it does.
    /// </remarks>
    void Use(string folder);
}
