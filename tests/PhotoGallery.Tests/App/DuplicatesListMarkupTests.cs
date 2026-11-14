using System.Text.RegularExpressions;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The duplicates list, read as text: whether it virtualises is decided by its
/// markup alone, and nothing fails when it stops - the window just freezes.
/// </summary>
public sealed class DuplicatesListMarkupTests
{
    private static readonly string s_window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    [Fact]
    public void TheListBuildsOnlyTheSetsOnScreen()
    {
        // Switching to "Looks the same" with 503 sets froze the window for four
        // seconds while every set and every photograph in it was built.
        Match list = Regex.Match(
            s_window,
            "<ItemsControl Grid\\.Row=\"2\"[^>]*ItemsSource=\"\\{Binding Duplicates\\.Showing\\}\"[^>]*>");

        Assert.True(list.Success, "The duplicates list is no longer where this test looks for it.");
        Assert.Contains("ScrollViewer.CanContentScroll=\"True\"", list.Value, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", list.Value, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.ScrollUnit=\"Pixel\"", list.Value, StringComparison.Ordinal);

        // Its own scroller, and a virtualising panel inside it. Wrapped in an
        // outside ScrollViewer instead, the list is given infinite height and
        // builds everything regardless of the settings above.
        string after = s_window[list.Index..];
        string listMarkup = after[..after.IndexOf("</ItemsControl.ItemTemplate>", StringComparison.Ordinal)];
        Assert.Contains("<ItemsPresenter />", listMarkup, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel />", listMarkup, StringComparison.Ordinal);
    }
}
