using System.Text.RegularExpressions;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The library's "No pictures yet" line, read as text: which property shows it
/// is decided by its binding alone, and a binding that goes wrong fails silently.
/// </summary>
public sealed class LibraryEmptyMessageMarkupTests
{
    private static readonly string s_window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

    [Fact]
    public void TheEmptyMessageWaitsForTheReadToAnswer()
    {
        // Bound to IsEmpty, it stood over the fade for the whole of the first
        // read of a session, and over freshly drawn tiles until the prepared
        // check caught up.
        Match message = Regex.Match(
            s_window,
            "<TextBlock[^>]*Text=\"\\{Binding Gallery\\.EmptyMessage\\}\"[^>]*/>");

        Assert.True(message.Success, "The library's empty message is no longer where this test looks for it.");
        Assert.Contains("Visibility=\"{Binding Gallery.ShowsEmptyMessage,", message.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Gallery.IsEmpty", s_window, StringComparison.Ordinal);
    }
}
