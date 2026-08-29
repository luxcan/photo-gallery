namespace PhotoGallery.Tests.App;

/// <summary>
/// Where the app's own source files are, for the tests that read them as text.
/// </summary>
/// <remarks>
/// Several tests here check rules that live in markup rather than in behaviour -
/// that the two themes are key-for-key, that the neutral ramp is ordered, that
/// everything floating over the app is the same object. A view-model suite is
/// blind to all of it, so those tests open the XAML and read it, which means
/// each of them has to find the repository from wherever the test binary is.
///
/// <para>Three of them had written that walk out separately before this existed,
/// each with its own idea of what marked the root. One walk, so a folder that
/// moves breaks in one place.</para>
/// </remarks>
internal static class AppMarkup
{
    private static readonly string s_root = FindRoot();

    /// <summary>
    /// The full path of a file under <c>src/PhotoGallery.App</c>, named in
    /// segments: <c>PathTo("Theme", "Dark.xaml")</c>.
    /// </summary>
    internal static string PathTo(params string[] relative) =>
        Path.Combine([s_root, .. relative]);

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !Directory.Exists(Path.Combine(directory.FullName, "src", "PhotoGallery.App")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                "Repository root not found above the test binary.")
            : Path.Combine(directory.FullName, "src", "PhotoGallery.App");
    }
}
