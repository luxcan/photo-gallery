using System.Reflection;
using PhotoGallery.App.Collections;
using PhotoGallery.App.Duplicates;
using PhotoGallery.App.Gallery;
using PhotoGallery.App.People;

namespace PhotoGallery.Tests.App;

/// <summary>
/// Every list drawn over a picture hands the keys and the focus back.
/// </summary>
/// <remarks>
/// Left and right reach a picture by bubbling out of whatever holds the focus,
/// and a list drawn over it takes the focus into itself. Closing one takes the
/// element that held it out of the visual tree, and the focus with it: the
/// picture is still open and still looks ready, and the arrows do nothing until
/// something inside is clicked again. The mirror of it is the keys themselves -
/// while a list is up they belong to the list, or typing a name walks the
/// library underneath.
///
/// <para>Both halves were known, written down in a comment, and handled for the
/// two name lists. The album list was added later and got neither, so putting a
/// photograph in an album stopped the arrows where naming a face in the same
/// viewer did not. Fixing that one list is worth very little: the next list
/// added will miss the same two lines, for the same reason, and the failure will
/// look exactly like this one - the app works, nothing throws, and the arrows
/// are simply dead.</para>
///
/// <para>So these tests do not name the three lists that exist. They find every
/// list the view models expose and demand both lines for each, which is the only
/// version of this test that a fourth list cannot walk past. It is read as source
/// because focus and a key handler are not behaviour any view-model test can
/// reach.</para>
/// </remarks>
public sealed class ViewerFocusTests
{
    /// <summary>
    /// Where each screen's pictures are, for the two rules below: the element
    /// that must get the focus back, and the handler that reads the keys over it.
    /// </summary>
    private static readonly Dictionary<Type, (string Handler, string Focus)> s_surfaces =
        new()
        {
            [typeof(GalleryViewModel)] = ("OnViewerKeyDown", "PhotoViewer"),
            [typeof(PeopleViewModel)] = ("OnInspectorKeyDown", "FaceInspector"),
            [typeof(DuplicatesViewModel)] = ("OnDuplicateInspectorKeyDown", "DuplicateInspector"),
        };

    /// <summary>How the window names each screen's view model.</summary>
    private static readonly (string Path, Type Owner)[] s_screens =
    [
        ("viewModel.Gallery", typeof(GalleryViewModel)),
        ("viewModel.People", typeof(PeopleViewModel)),
        ("viewModel.Duplicates", typeof(DuplicatesViewModel)),
        ("viewModel.Collections", typeof(CollectionsViewModel)),
    ];

    /// <summary>
    /// The two rules are worth nothing if the search for lists finds none.
    /// </summary>
    /// <remarks>
    /// A renamed property or a moved type would empty the list quietly, and both
    /// tests below would go green over an app with no wiring at all.
    /// </remarks>
    [Fact]
    public void ThereAreListsToCheck()
    {
        IReadOnlyList<Picker> found = Pickers();

        Assert.True(
            found.Count >= 3,
            $"Expected the screens to expose lists that open over a picture; found {found.Count}.");
    }

    /// <summary>
    /// Every list hands the focus back to its own screen's picture when it closes.
    /// </summary>
    [Fact]
    public void EveryListHandsTheFocusBack()
    {
        string[] calls = Calls(Constructor(), "WhenPickingEnds(");
        List<string> wrong = [];

        foreach (Picker picker in Pickers())
        {
            string? call = calls.FirstOrDefault(
                one => one.Contains(picker.Path, StringComparison.Ordinal));

            if (call is null)
            {
                wrong.Add($"{picker.Path} is never handed the focus back: it needs a "
                        + "WhenPickingEnds call, or closing it leaves the arrow keys dead.");
            }
            else if (!call.Contains(picker.Focus, StringComparison.Ordinal))
            {
                wrong.Add($"{picker.Path} hands the focus to something other than "
                        + $"{picker.Focus}, which is the picture it is drawn over.");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// And has first claim on the arrows and Escape while it is up.
    /// </summary>
    /// <remarks>
    /// Before the keys are acted on, not merely somewhere in the handler: read
    /// afterwards it is not a guard, and the picture steps out from under the
    /// list on the first arrow.
    /// </remarks>
    [Fact]
    public void EveryListHasFirstClaimOnTheKeys()
    {
        string source = Source();
        List<string> wrong = [];

        foreach (Picker picker in Pickers())
        {
            string handler = Handler(source, picker.Handler);
            int guard = handler.IndexOf(
                $"{picker.Property}.IsOpen", StringComparison.Ordinal);

            if (guard < 0)
            {
                wrong.Add($"{picker.Path} is not asked about in {picker.Handler}, so the "
                        + "arrows walk the pictures underneath it while it is open.");
                continue;
            }

            int steps = handler.IndexOf("case Key.Left:", StringComparison.Ordinal);
            if (steps >= 0 && steps < guard)
            {
                wrong.Add($"{picker.Handler} steps before it asks about {picker.Path}, "
                        + "so the guard is not a guard.");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// And is put down by Escape, from the one place that decides what Escape does.
    /// </summary>
    /// <remarks>
    /// Each screen's key handler used to carry an Escape branch of its own for
    /// the list drawn over it, which is three copies of one rule and is why the
    /// two album panels - which have no such handler - could not be escaped at
    /// all. The rule now lives in <c>Dismissible</c>, so a fourth list has one
    /// place to be added to rather than three to be copied into.
    /// </remarks>
    [Fact]
    public void EveryListIsPutDownByEscape()
    {
        string dismissible = Body(Source(), "> Dismissible()");
        List<string> wrong = [];

        Assert.True(
            dismissible.Length > 0,
            "Dismissible has been renamed or removed; nothing decides what Escape closes.");

        foreach (Picker picker in Pickers())
        {
            if (!dismissible.Contains($"{picker.Property}.IsOpen", StringComparison.Ordinal))
            {
                wrong.Add($"{picker.Path} is not in Dismissible, so Escape does nothing to it "
                        + "and the user has to find its Cancel button.");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// Every list this window could have to wire, found rather than listed.
    /// </summary>
    /// <remarks>
    /// A list is anything a screen hands out that can be opened over a picture,
    /// which in this app means it says whether it is open and can be told to
    /// stop asking. Recognising them by shape is what makes a fourth one appear
    /// here on the day it is written rather than on the day it is reported.
    /// </remarks>
    private static IReadOnlyList<Picker> Pickers()
    {
        List<Picker> found = [];

        foreach ((string path, Type owner) in s_screens)
        {
            foreach (PropertyInfo property in owner.GetProperties())
            {
                if (!IsPicker(property.PropertyType))
                {
                    continue;
                }

                Assert.True(
                    s_surfaces.ContainsKey(owner),
                    $"{owner.Name} now has a list ({property.Name}) and no picture is mapped "
                    + "to it. Say which element owns the focus and which handler owns the "
                    + "keys, or the list will close onto nothing.");

                (string handler, string focus) = s_surfaces[owner];
                found.Add(new Picker(
                    $"{path}.{property.Name}", property.Name, handler, focus));
            }
        }

        return found;
    }

    private static bool IsPicker(Type type) =>
        type.GetProperty("IsOpen")?.PropertyType == typeof(bool)
        && type.GetProperty("CancelCommand") is not null;

    private static string Source() =>
        File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));

    /// <summary>The window's constructor, where the lists are wired up.</summary>
    private static string Constructor() => Body(Source(), "public MainWindow(");

    private static string Handler(string source, string name)
    {
        string body = Body(source, $"{name}(object");

        Assert.True(
            body.Length > 0,
            $"{name} has been renamed or removed; the rule it carried is unguarded.");

        return body;
    }

    /// <summary>One member, from its declaration to the start of the next one.</summary>
    private static string Body(string source, string declaration)
    {
        int start = source.IndexOf(declaration, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        int end = source.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
        int next = source.IndexOf("\n    private ", start, StringComparison.Ordinal);

        if (next >= 0 && (end < 0 || next < end))
        {
            end = next;
        }

        return end < 0 ? source[start..] : source[start..end];
    }

    /// <summary>The arguments of every call to one method, one string each.</summary>
    private static string[] Calls(string source, string method)
    {
        List<string> calls = [];

        for (int at = source.IndexOf(method, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(method, at + method.Length, StringComparison.Ordinal))
        {
            int end = source.IndexOf(");", at, StringComparison.Ordinal);
            calls.Add(end < 0 ? source[at..] : source[at..end]);
        }

        return [.. calls];
    }

    private sealed record Picker(string Path, string Property, string Handler, string Focus);
}
