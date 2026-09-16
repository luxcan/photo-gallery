using System.Reflection;
using System.Text.RegularExpressions;
using PhotoGallery.App.ViewModels;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Every binding in the window that starts at the window's own view model
/// reaches a member that is actually there.
/// </summary>
/// <remarks>
/// A WPF binding to a property that does not exist fails silently: the control
/// draws empty, the app carries on, and the only record of it is a trace message
/// nobody is watching. Every other markup rule in this suite is guarded by a
/// test that reads the XAML as text, and this is the one that was not.
///
/// <para>Written when the domain word moved from Collection to Album, which
/// rewrote a hundred of these paths in one pass. A compiler checks none of them.
/// </para>
///
/// <para>Only paths whose first segment is a property of
/// <see cref="MainViewModel"/> are followed. The rest belong to a template whose
/// data context is a row rather than the window - a binding of "Name" is a claim
/// about whatever that template is repeating, not about the view model, and this
/// file cannot know which type that is without interpreting the templates.</para>
/// </remarks>
public sealed class BindingPathTests
{
    /// <summary>
    /// A path out of a binding expression: the whole of <c>{Binding A.B.C}</c>
    /// and the <c>Path=</c> of a longer one.
    /// </summary>
    /// <remarks>
    /// Deliberately refuses anything with a bracket, a slash or a parenthesis.
    /// Indexers, attached properties and hierarchical paths are a different
    /// language from a chain of property names, and a partial reading of one
    /// would report a member missing that was never named.
    /// </remarks>
    private static readonly Regex s_path = new(
        @"\{Binding\s+(?:Path=)?(?<path>[A-Za-z_][A-Za-z0-9_.]*)|Path=(?<path>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.Compiled);

    [Fact]
    public void EveryPathRootedInTheViewModelReachesAMember()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        List<string> broken = [];
        HashSet<string> seen = [];

        foreach (Match match in s_path.Matches(markup))
        {
            string path = match.Groups["path"].Value;

            // A binding written against the window from inside a template says
            // so, and the DataContext step is not a member of anything.
            if (path.StartsWith("DataContext.", StringComparison.Ordinal))
            {
                path = path["DataContext.".Length..];
            }

            string[] segments = path.Split('.');
            if (segments.Length < 2 || !seen.Add(path))
            {
                continue;
            }

            Type? walker = typeof(MainViewModel);
            PropertyInfo? first = Find(walker, segments[0]);
            if (first is null)
            {
                // Rooted in a row rather than in the window. Not this test's
                // business - see the remarks.
                continue;
            }

            walker = first.PropertyType;

            for (int i = 1; i < segments.Length; i++)
            {
                PropertyInfo? next = Find(walker, segments[i]);
                if (next is null)
                {
                    broken.Add($"{path} (no {segments[i]} on {walker!.Name})");
                    break;
                }

                walker = next.PropertyType;
            }
        }

        Assert.Empty(broken);
    }

    /// <summary>
    /// A binding that writes back writes to something that can be written to.
    /// </summary>
    /// <remarks>
    /// Unlike a path that names nothing, this one does not fail quietly: WPF
    /// throws while the window is being built, so the app does not start at all.
    /// It cost a start-up crash to learn, on a popup whose IsOpen was bound
    /// two-way - as a popup's IsOpen usually must be - to a property that was
    /// computed from two others and therefore had no setter.
    ///
    /// <para>Nothing catches it earlier. The markup compiles, every text-scanning
    /// test in this suite reads the string and is satisfied, and the path itself
    /// resolves perfectly well - it is only the direction that is impossible.</para>
    ///
    /// <para>Declared modes only. A TextBox's Text and a ToggleButton's IsChecked
    /// are two-way by default and are not written here, so knowing which of those
    /// bind back would mean knowing each target property's default mode - a
    /// bigger reading of the markup than this file does.</para>
    /// </remarks>
    [Fact]
    public void EveryBindingThatWritesBackReachesAPropertyThatCanBeSet()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        List<string> broken = [];
        int checked_ = 0;

        foreach (Match match in s_binding.Matches(markup))
        {
            string rest = match.Groups["rest"].Value;

            if (!rest.Contains("Mode=TwoWay", StringComparison.Ordinal)
                && !rest.Contains("Mode=OneWayToSource", StringComparison.Ordinal))
            {
                continue;
            }

            if (Resolve(match.Groups["path"].Value) is not PropertyInfo target)
            {
                // Rooted in a row rather than in the window, or naming nothing -
                // which is the test above's business, not this one's.
                continue;
            }

            checked_++;

            if (target.SetMethod is null || !target.SetMethod.IsPublic)
            {
                broken.Add($"{match.Groups["path"].Value} ({target.Name} has no setter)");
            }
        }

        Assert.Empty(broken);

        // And the walk found some, or an unreadable file would pass in silence.
        Assert.True(checked_ >= 5, $"only {checked_} writing bindings were followed");
    }

    /// <summary>
    /// The whole of a binding expression, allowing one level of nesting so that
    /// a Mode written after a converter is still seen.
    /// </summary>
    private static readonly Regex s_binding = new(
        @"\{Binding\s+(?:Path=)?(?<path>[A-Za-z_][A-Za-z0-9_.]*)(?<rest>(?:[^{}]|\{[^}]*\})*)\}",
        RegexOptions.Compiled);

    /// <summary>
    /// The property a path ends at, or null when it is not the window's to
    /// resolve.
    /// </summary>
    private static PropertyInfo? Resolve(string path)
    {
        if (path.StartsWith("DataContext.", StringComparison.Ordinal))
        {
            path = path["DataContext.".Length..];
        }

        Type? walker = typeof(MainViewModel);
        PropertyInfo? found = null;

        foreach (string segment in path.Split('.'))
        {
            found = Find(walker, segment);
            if (found is null)
            {
                return null;
            }

            walker = found.PropertyType;
        }

        return found;
    }

    /// <summary>
    /// Sanity: the paths this test follows are not an empty set.
    /// </summary>
    /// <remarks>
    /// Without this the test above passes just as happily against markup it
    /// failed to parse at all, which is the way a text-scanning test usually
    /// goes quietly wrong.
    /// </remarks>
    [Fact]
    public void TheWindowBindsToTheAlbumsScreenAtAll()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        List<string> albums =
        [
            .. s_path.Matches(markup)
                .Select(match => match.Groups["path"].Value)
                .Where(path => path.StartsWith("Albums.", StringComparison.Ordinal))
        ];

        Assert.True(albums.Count > 20, $"only {albums.Count} paths into the albums screen");
    }

    /// <summary>
    /// The property behind one segment, on the type or on any interface it
    /// carries.
    /// </summary>
    /// <remarks>
    /// The interfaces matter because a view model exposes several of these
    /// chains as a sequence of something, and a binding on to Count reaches it
    /// through the interface rather than the class.
    /// </remarks>
    private static PropertyInfo? Find(Type? type, string name)
    {
        if (type is null)
        {
            return null;
        }

        PropertyInfo? declared = type.GetProperty(
            name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

        return declared ?? type.GetInterfaces()
            .Select(contract => contract.GetProperty(
                name, BindingFlags.Public | BindingFlags.Instance))
            .FirstOrDefault(found => found is not null);
    }
}
