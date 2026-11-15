namespace PhotoGallery.Tests.App;

/// <summary>
/// The small bar in the bottom-right corner that says a suggestion round is
/// running, read from the markup.
/// </summary>
/// <remarks>
/// A view-model test cannot see where a thing is drawn or whether it takes
/// clicks, and this bar exists precisely because a blocking modal was "too
/// disruptive": it has to sit out of the way, take nothing from the screen
/// under it, and stay clear of the photo viewer's own buttons in that corner.
/// </remarks>
public sealed class SuggestionsBarMarkupTests
{
    private static readonly string s_window =
        File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    [Fact]
    public void TheBarIsBoundToTheRunner()
    {
        string bar = Bar();

        Assert.Contains("Visibility=\"{Binding Suggestions.IsVisible,", bar, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Suggestions.Caption}\"", bar, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Suggestions.Count}\"", bar, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding Suggestions.HasCount,", bar, StringComparison.Ordinal);

        // A thin bar that moves rather than a count it cannot know, and none
        // under a failure, which is not work in progress.
        Assert.Contains("IsIndeterminate=\"True\"", bar, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding Suggestions.HasFailed,", bar, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBarTakesNoClicks()
    {
        // Nothing to press, and nothing under it may be blocked by it.
        Assert.Contains("IsHitTestVisible=\"False\"", Bar(), StringComparison.Ordinal);
        Assert.DoesNotContain("<Button", Bar(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBarSitsInTheBottomRightCornerAboveTheViewerButStillUnderThePassOverlay()
    {
        int corner = s_window.IndexOf(
            "<StackPanel Grid.Row=\"0\" Margin=\"{Binding NoticeMargin}\"", StringComparison.Ordinal);
        int bar = s_window.IndexOf("x:Name=\"SuggestionsBar\"", StringComparison.Ordinal);
        int viewer = s_window.IndexOf("x:Name=\"PhotoViewer\"", StringComparison.Ordinal);
        int overlay = s_window.LastIndexOf(
            "Visibility=\"{Binding IsOverlayVisible,", StringComparison.Ordinal);

        Assert.True(corner > 0, "the corner the notice and the bar share is gone");
        Assert.True(bar > corner, "the bar is not in the shared corner");

        string cornerTag = s_window[corner..s_window.IndexOf('>', corner)];
        Assert.Contains("HorizontalAlignment=\"Right\"", cornerTag, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Bottom\"", cornerTag, StringComparison.Ordinal);

        // Later siblings paint over earlier ones: over an open photograph, and
        // under a long pass, which outranks it.
        Assert.True(bar > viewer, "the viewer would cover the bar");
        Assert.True(bar < overlay, "the bar would be drawn over a long pass");
    }

    /// <summary>The bar's own element, from its name to its closing tag.</summary>
    private static string Bar()
    {
        int start = s_window.IndexOf("x:Name=\"SuggestionsBar\"", StringComparison.Ordinal);
        Assert.True(start > 0, "the suggestions bar is not in the window");

        int end = s_window.IndexOf("</Border>", start, StringComparison.Ordinal);
        return s_window[start..end];
    }
}
