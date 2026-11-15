using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Gallery;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.App;

/// <summary>The loading card's second line: what the wall is being read for.</summary>
public sealed class LibraryLoadingTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pg-loading-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly GalleryViewModel _gallery;

    public LibraryLoadingTargetTests()
    {
        var working = new WorkingFolder(_root);
        working.EnsureCreated();
        _gallery = new GalleryViewModel(
            _services.GetRequiredService<IServiceScopeFactory>(), new FileSystemThumbnailStore(working));
    }

    [Fact]
    public void WithNoFilterItIsEverything() =>
        Assert.Equal("Everything", _gallery.LoadingTarget);

    [Fact]
    public void ItNamesTheChipsInTheirOwnWords()
    {
        _gallery.Kind = AssetKind.Video;
        _gallery.OutsideAlbums = true;

        Assert.Equal("Videos · Not in an album", _gallery.LoadingTarget);
    }

    [Fact]
    public void ItIsAskedAgainWheneverLoadingStarts()
    {
        var asked = new List<string?>();
        _gallery.PropertyChanged += (_, e) => asked.Add(e.PropertyName);

        _gallery.IsLoading = true;

        Assert.Contains(nameof(GalleryViewModel.LoadingTarget), asked);
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
}
