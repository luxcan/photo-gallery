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
