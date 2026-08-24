using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The theme system mirrors its light and dark halves key-for-key, so any
/// combination proven readable in one theme exists in the other, and a swap can
/// never hit a missing resource. This test turns that rule into a failure
/// instead of a comment.
/// </summary>
public sealed class ThemeParityTests
{
    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void DarkAndLightDefineExactlyTheSameKeys()
    {
        HashSet<string> dark = KeysOf("Dark.xaml");
        HashSet<string> light = KeysOf("Light.xaml");

        List<string> onlyDark = dark.Except(light).Order().ToList();
        List<string> onlyLight = light.Except(dark).Order().ToList();

        Assert.True(onlyDark.Count == 0 && onlyLight.Count == 0,
            $"Theme keys out of parity. Only in Dark: [{string.Join(", ", onlyDark)}] "
          + $"Only in Light: [{string.Join(", ", onlyLight)}]");
    }

    [Fact]
    public void ThemesAreNotTrivial()
    {
        // Guards against the parity test passing because a path change made
        // both sets empty.
        Assert.True(KeysOf("Dark.xaml").Count > 20);
    }

    private static HashSet<string> KeysOf(string fileName)
    {
        string path = Path.Combine(FindRepoRoot(), "src", "PhotoGallery.App", "Theme", fileName);
        XDocument document = XDocument.Load(path);

        return document.Root!
            .Elements()
            .Select(e => e.Attribute(s_x + "Key")?.Value)
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PhotoGallery.sln")))
        {
            directory = directory.Parent!;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root not found above the test binary.");
    }
}
