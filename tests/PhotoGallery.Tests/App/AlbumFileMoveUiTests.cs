namespace PhotoGallery.Tests.App;

/// <summary>The album's physical move stays behind a preview and confirmation.</summary>
public sealed class AlbumFileMoveUiTests
{
    [Fact]
    public void TheButtonUsesTheGuardedClickHandler()
    {
        string markup = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.Contains("Move originals to a folder...", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnMoveAlbumClicked\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingMovesUntilThePlanHasBeenShownAndConfirmed()
    {
        string handler = Handler();
        int planned = handler.IndexOf("PlanAlbumMoveAsync", StringComparison.Ordinal);
        int asked = handler.IndexOf("AppDialog.Confirm", StringComparison.Ordinal);
        int moved = handler.IndexOf("MoveAlbumAsync", StringComparison.Ordinal);

        Assert.True(planned >= 0, "The destination is not checked before moving.");
        Assert.True(asked > planned, "The confirmation is shown before its move plan exists.");
        Assert.True(moved > asked, "The originals move before the confirmation is answered.");
    }

    [Fact]
    public void TheQuestionPromisesNoOverwriteAndPreservedLibraryInformation()
    {
        string handler = Handler();

        Assert.Contains("never overwritten", handler, StringComparison.Ordinal);
        Assert.Contains("keeps its album", handler, StringComparison.Ordinal);
        Assert.Contains("faces, metadata", handler, StringComparison.Ordinal);
    }

    private static string Handler()
    {
        string code = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml.cs"));
        int start = code.IndexOf("OnMoveAlbumClicked(", StringComparison.Ordinal);
        Assert.True(start >= 0, "The move handler has been renamed or removed.");

        int next = code.IndexOf("\n    private ", start, StringComparison.Ordinal);
        return next < 0 ? code[start..] : code[start..next];
    }
}
