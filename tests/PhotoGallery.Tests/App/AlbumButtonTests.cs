using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Albums;
using PhotoGallery.App.Gallery;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The one control in the viewer that says which album a photograph is in.
/// </summary>
/// <remarks>
/// It was three things: a caption reading "In Taiwan", a list button that opened
/// the albums, and a tick that took the photograph out of the one it was in. The
/// tick is the part that gave it away - a tick means confirm everywhere else in
/// this app, and here it meant remove. The caption said the album's name, the
/// button beside it did not, and neither said what the other was for.
///
/// <para>Now one button carries the name and opens the list, and the way out is
/// inside that list, because "none of them" is an answer to which album this is
/// in rather than a separate act.</para>
/// </remarks>
public sealed class AlbumButtonTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly string _root;
    private readonly GalleryViewModel _gallery;

    public AlbumButtonTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-album-button-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var workingFolder = new WorkingFolder(_root);
        workingFolder.EnsureCreated();

        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new FileSystemThumbnailStore(workingFolder));
    }

    /// <summary>A photograph in no album is offered one, on the same button.</summary>
    [Fact]
    public void WithNoAlbum_TheButtonOffersOne()
    {
        Assert.Equal("Add to an album", _gallery.AlbumLabel);
        Assert.False(_gallery.IsInAnAlbum);
    }

    /// <summary>In one, the button is its name and nothing else.</summary>
    /// <remarks>
    /// Not "In Taiwan". The glyph beside it already says album, and a button
    /// whose label is a sentence about itself reads as a caption that happens to
    /// be clickable.
    /// </remarks>
    [Fact]
    public void InAnAlbum_TheButtonIsItsName()
    {
        _gallery.OpenPhotoAlbum = Album(7, "Taiwan");

        Assert.Equal("Taiwan", _gallery.AlbumLabel);
        Assert.Contains("Taiwan", _gallery.AlbumTip, StringComparison.Ordinal);
    }

    /// <summary>
    /// The notice says only what the button cannot.
    /// </summary>
    /// <remarks>
    /// It used to fall back to "In Taiwan" whenever nothing had just happened,
    /// which is the word the button now carries - the same thing said twice,
    /// side by side.
    /// </remarks>
    [Fact]
    public void TheNoticeIsSilentUntilSomethingHappens()
    {
        _gallery.OpenPhotoAlbum = Album(7, "Taiwan");

        Assert.False(_gallery.HasAlbumNotice);

        _gallery.AlbumNotice = "Moved into Taiwan, out of Genting";

        Assert.True(_gallery.HasAlbumNotice);
    }

    /// <summary>The way out is offered by the list, named after the album.</summary>
    [Fact]
    public void TheListOffersTheWayOutOfTheAlbumItIsIn()
    {
        AlbumPicker picker = Open(current: 7);

        Assert.True(picker.IsInOne);
        Assert.Equal("Take it out of Taiwan", picker.TakeOutLabel);
        Assert.True(picker.TakeOutCommand.CanExecute(null));
    }

    /// <summary>And does not offer it when there is nothing to leave.</summary>
    [Fact]
    public void APhotographInNoAlbumIsOfferedNoWayOut()
    {
        AlbumPicker picker = Open(current: 0);

        Assert.False(picker.IsInOne);
        Assert.False(picker.TakeOutCommand.CanExecute(null));
    }

    /// <summary>
    /// Typing keeps the way out, even when it hides the album being left.
    /// </summary>
    /// <remarks>
    /// The reason the name is held rather than read off whichever choice is
    /// marked: three letters that Taiwan does not contain take Taiwan out of the
    /// list, and the way out of it would go with it.
    /// </remarks>
    [Fact]
    public void TypingDoesNotTakeTheWayOutWithIt()
    {
        AlbumPicker picker = Open(current: 7);

        picker.Typed = "zzz";

        Assert.Empty(picker.Choices);
        Assert.True(picker.IsInOne);
        Assert.Equal("Take it out of Taiwan", picker.TakeOutLabel);
    }

    /// <summary>Pressing it is what asks for the removal.</summary>
    [Fact]
    public async Task TakingItOutAsksTheScreenToDoIt()
    {
        bool asked = false;

        var picker = new AlbumPicker(
            _ => Task.CompletedTask,
            () =>
            {
                asked = true;
                return Task.CompletedTask;
            });

        picker.Open([Album(7, "Taiwan")], current: 7, "Which album", "Pick one");
        await picker.TakeOutCommand.ExecuteAsync(null);

        Assert.True(asked);
    }

    /// <summary>Opening on another photograph forgets the album the last one was in.</summary>
    [Fact]
    public void TheWayOutIsReReadEachTimeTheListOpens()
    {
        AlbumPicker picker = Open(current: 7);
        picker.Close();

        picker.Open([Album(7, "Taiwan")], current: 0, "Which album", "Pick one");

        Assert.False(picker.IsInOne);
    }

    /// <summary>
    /// The strip has one control for albums, and it is the one that names it.
    /// </summary>
    /// <remarks>
    /// Markup, so no view-model test can see it: every property below can be
    /// perfect while the strip still carries three controls bound to none of
    /// them.
    /// </remarks>
    [Fact]
    public void TheStripHasOneAlbumControl()
    {
        string window = File.ReadAllText(AppMarkup.PathTo("Shell", "MainWindow.xaml"));

        Assert.Contains("Gallery.AlbumLabel", window, StringComparison.Ordinal);
        Assert.Contains("Gallery.AlbumTip", window, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(window, "Gallery.AddToAlbumCommand"));

        // The tick and the caption that said the name a second time.
        Assert.DoesNotContain("TakeOutOfAlbumCommand", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Gallery.AlbumCaption", window, StringComparison.Ordinal);
    }

    /// <summary>And the way out is in the list the button opens.</summary>
    [Fact]
    public void TheWayOutIsInTheList()
    {
        string controls = File.ReadAllText(AppMarkup.PathTo("Theme", "Controls.xaml"));

        Assert.Contains("TakeOutCommand", controls, StringComparison.Ordinal);
        Assert.Contains("TakeOutLabel", controls, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _services.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not go is not a failed test.
        }
    }

    private static AlbumPicker Open(int current)
    {
        var picker = new AlbumPicker(_ => Task.CompletedTask, () => Task.CompletedTask);
        picker.Open([Album(7, "Taiwan"), Album(8, "Genting")], current, "Which album", "Pick one");
        return picker;
    }

    private static AlbumSummary Album(int id, string name) =>
        new(id, name, DateTime.UnixEpoch, DateTime.UnixEpoch,
            AlbumKind.Event, AlbumOrigin.Made, 0, CoverThumbnailName: null);

    private static int Occurrences(string text, string value)
    {
        int found = 0;

        for (int at = text.IndexOf(value, StringComparison.Ordinal);
             at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            found++;
        }

        return found;
    }
}
