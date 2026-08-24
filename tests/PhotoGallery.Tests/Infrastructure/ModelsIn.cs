using PhotoGallery.Application.Ports;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// A models folder in a temporary directory, movable but not remembered.
/// </summary>
/// <remarks>
/// The real one writes the choice into the config file beside the executable,
/// which a test has no business touching. What matters here is only that moving
/// the folder changes where the store looks.
/// </remarks>
public sealed class ModelsIn : IModelFolder
{
    public ModelsIn(string path)
    {
        Default = path;
        Path = path;
    }

    public string Path { get; private set; }

    public string Default { get; }

    public bool WasChosen => !string.Equals(Path, Default, StringComparison.Ordinal);

    public void Use(string folder) => Path = folder;
}
