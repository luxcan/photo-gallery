using System.Xml.Linq;

namespace PhotoGallery.Tests.App;

/// <summary>
/// That the open photograph covers the whole window, and that the panel beside
/// it carries the two controls about the picture.
/// </summary>
/// <remarks>
/// The viewer used to be a child of the content column, so a photograph opened
/// with 196 DIP of side nav and a 22 DIP status bar still around it. Moving it to
/// the root grid is three attributes and one position in document order, none of
/// which throws when it is wrong: put it before the status bar and the strip
/// shows through, put it after the pass overlay and a long pass draws underneath
/// the picture, forget the span and it has the height of one row.
///
/// <para>The album button and the Names toggle came out of the row of tools at
/// the same time. A WPF binding to a property that is not there fails silently,
/// so a mistyped <c>Grid.Row</c> on either of them shows as a control that is
/// simply absent - which is exactly what these two assertions are for.</para>
/// </remarks>
public sealed class ViewerLayoutTests
{
    private static readonly XNamespace s_x = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ThePictureIsAChildOfTheWindowRatherThanOfTheContentColumn()
    {
        XElement viewer = RootChildren().Single(IsViewer);

        Assert.Equal("0", (string?)viewer.Attribute("Grid.Row"));
        Assert.Equal("2", (string?)viewer.Attribute("Grid.RowSpan"));
    }

    [Fact]
    public void ItCoversTheStatusBarAndIsItselfCoveredByALongPass()
    {
        // Later siblings paint over earlier ones, so this order is the whole
        // mechanism. There is nothing else saying it.
        List<XElement> children = [.. RootChildren()];

        int statusBar = children.FindIndex(
            e => (string?)e.Attribute("Background") == "{DynamicResource StatusBar.Background}");
        int viewer = children.FindIndex(IsViewer);
        int overlay = children.FindIndex(
            e => (string?)e.Attribute("Background") == "{DynamicResource ModalScrim}");

        Assert.True(statusBar >= 0 && viewer >= 0 && overlay >= 0);
        Assert.True(statusBar < viewer, "the viewer must be drawn after the status bar to cover it");
        Assert.True(viewer < overlay, "a long pass must still be drawn over the viewer");
    }

    [Fact]
    public void TheRootGridHasAsManyRowsAsTheViewerSpans()
    {
        // The same rule InspectorSpanTests holds for the two inspectors: a span
        // short of the row count leaves the strip showing, and one past it is a
        // silent no-op that the next row added would turn into a gap.
        XElement root = RootGrid();
        int rows = root.Elements()
            .Single(e => e.Name.LocalName == "Grid.RowDefinitions")
            .Elements()
            .Count();

        XElement viewer = RootChildren().Single(IsViewer);
        int first = int.Parse((string)viewer.Attribute("Grid.Row")!);
        int span = int.Parse((string)viewer.Attribute("Grid.RowSpan")!);

        Assert.Equal(rows, first + span);
    }

    [Fact]
    public void TheAlbumAndTheNamesToggleSitAboveTheFacts()
    {
        // Both moved out of the bottom row into the head of the details panel,
        // and the panel's own scroller must not be able to take them off screen.
        // Read as a tree, not as text: comparing offsets in the file only proves
        // one string is typed before another, which stays true however wrongly
        // the two controls are parented.
        XElement panel = RootChildren()
            .Single(IsViewer)
            .Descendants()
            .Single(e => e.Name.LocalName == "Grid" && (string?)e.Attribute("Width") == "290");

        XElement header = panel.Elements()
            .Single(e => e.Name.LocalName == "Grid" && (string?)e.Attribute("Grid.Row") == "0");
        XElement scroller = panel.Elements()
            .Single(e => e.Name.LocalName == "ScrollViewer");

        Assert.Contains(
            header.Descendants(),
            e => (string?)e.Attribute("Command") == "{Binding Gallery.AddToAlbumCommand}");
        Assert.Contains(
            header.Descendants(),
            e => (string?)e.Attribute("IsChecked") == "{Binding Gallery.ShowFaceNames, Mode=TwoWay}");

        // The facts scroll; the two controls above them do not.
        Assert.Contains(
            scroller.Descendants(),
            e => (string?)e.Attribute("Content") == "{Binding Gallery.OpenDetails}");
        Assert.DoesNotContain(
            scroller.Descendants(),
            e => (string?)e.Attribute("Command") == "{Binding Gallery.AddToAlbumCommand}");
    }

    [Fact]
    public void TheArrowsShareTheirColumnWithThePicture()
    {
        // The bottom row repeats the row above it, so the arrows centre under the
        // picture rather than under the picture plus its gap. The top row keeps
        // the 16 inside its own column (the panel is 290 with a 16 margin), so
        // the bottom row's minimum has to be 306 - at 290 the two star columns
        // differ by the gap and the arrows sit 8 DIP off centre.
        XElement bottom = RootChildren()
            .Single(IsViewer)
            .Descendants()
            .Single(e => e.Name.LocalName == "Grid" && (string?)e.Attribute("MinHeight") == "38");

        XElement tools = bottom
            .Elements().Single(e => e.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Last();

        Assert.Equal("306", (string?)tools.Attribute("MinWidth"));
    }

    [Fact]
    public void TheFaceSummaryIsGone()
    {
        // "3 faces - click one to say who it is" used to head the tools. The
        // boxes on the picture say it, and say which; the handoff drops the
        // sentence, and the view-model property went with the binding.
        Assert.DoesNotContain("FaceSummary", Markup(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheToolsInTheCornerAreAllTheSameButton()
    {
        // Turn, turn, delete, play and stop are one shape at one size. Five
        // copies of the same four setters is how they drift apart - and the face
        // inspector's corner, which says in its own comment that it is the photo
        // viewer's corner, has to be the same three again rather than the older
        // wide button.
        int inViewer = RootChildren().Single(IsViewer).Descendants()
            .Count(e => (string?)e.Attribute("Style") == "{DynamicResource ViewerToolButton}");

        int everywhere = Markup().Split("{DynamicResource ViewerToolButton}").Length - 1;

        Assert.Equal(5, inViewer);
        Assert.Equal(8, everywhere);
    }

    private static string Markup() => File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    private static bool IsViewer(XElement element) =>
        (string?)element.Attribute(s_x + "Name") == "PhotoViewer";

    private static XElement RootGrid() =>
        XDocument.Load(AppMarkup.PathTo("Shell", "MainWindow.xaml"))
            .Root!
            .Elements()
            .Single(e => e.Name.LocalName == "Grid");

    private static IEnumerable<XElement> RootChildren() =>
        RootGrid().Elements().Where(e => e.Name.LocalName != "Grid.RowDefinitions");
}
