using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// An overlay that covers a screen must span every row of the grid it covers,
/// including the one sized in stars.
/// </summary>
/// <remarks>
/// FaceInspector spanned two rows of a three-row grid, and both of the two were
/// sized Auto. An overlay measured against Auto is given no height to fit into,
/// so the picture inside it sized itself to the width instead: on a maximised
/// window on a wide screen it grew tall enough to push the Yes/No buttons off
/// the bottom, and the screen said nothing about where they had gone. It looked
/// exactly like the controls had been deleted.
///
/// <para>Nothing about it is behaviour, so the whole view-model suite passed
/// throughout. It is also invisible in a small window, which is what let it ship
/// - the picture only outgrows the screen once the window is wide. Hence a test
/// that reads the markup and compares two numbers.</para>
/// </remarks>
public sealed class InspectorSpanTests
{
    private static readonly XNamespace s_wpf =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly XDocument s_window =
        XDocument.Load(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    [Theory]
    [InlineData("FaceInspector")]
    [InlineData("DuplicateInspector")]
    public void AnInspectorSpansEveryRowOfTheGridItCovers(string name)
    {
        XElement inspector = s_window.Descendants()
            .Single(element => (string?)element.Attribute(s_x + "Name") == name);

        XElement grid = inspector.Parent
            ?? throw new InvalidOperationException($"{name} has no parent grid.");

        int rows = grid.Elements(s_wpf + "Grid.RowDefinitions")
            .Elements(s_wpf + "RowDefinition")
            .Count();

        Assert.True(rows > 0, $"the grid holding {name} declares no rows");

        int first = int.Parse((string?)inspector.Attribute("Grid.Row") ?? "0");
        int span = int.Parse((string?)inspector.Attribute("Grid.RowSpan") ?? "1");

        Assert.Equal(rows, first + span);
    }

    [Fact]
    public void TheReviewKeepsItsAnswersOnScreen()
    {
        // The buttons the whole screen exists for. A span that does not reach the
        // star row pushes these off the bottom rather than removing them, so
        // their presence alone proves nothing - but their absence would.
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        foreach (string answer in new[]
                 {
                     "Yes, this is them", "No, it is...", "No, leave it out",
                 })
        {
            Assert.Contains(answer, markup, StringComparison.Ordinal);
        }
    }
}
