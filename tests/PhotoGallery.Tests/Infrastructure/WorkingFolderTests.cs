using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Infrastructure;

public sealed class WorkingFolderTests : IDisposable
{
    private readonly string _root;
    private readonly WorkingFolder _folder;

    public WorkingFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-wf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _folder = new WorkingFolder(_root);
    }

    [Fact]
    public void EnsureCreated_LaysOutEverySubfolder()
    {
        _folder.EnsureCreated();

        Assert.True(Directory.Exists(_folder.ThumbnailsPath));
        Assert.True(Directory.Exists(_folder.ModelsPath));
        Assert.True(Directory.Exists(_folder.QuarantinePath));
        Assert.True(Directory.Exists(_folder.LogsPath));
    }

    [Fact]
    public void IsAppOwned_TrueForTheAppsOwnFolders()
    {
        Assert.True(_folder.IsAppOwned(_folder.ThumbnailsPath));
        Assert.True(_folder.IsAppOwned(_folder.ModelsPath));
        Assert.True(_folder.IsAppOwned(_folder.QuarantinePath));
        Assert.True(_folder.IsAppOwned(_folder.LogsPath));
        Assert.True(_folder.IsAppOwned(Path.Combine(_folder.ThumbnailsPath, "a3")));
    }

    [Fact]
    public void IsAppOwned_FalseForTheRootItself()
    {
        // Set-up may point at a folder that already holds pictures, so the root
        // must remain usable as a photo source; only the app's own data is out.
        Assert.False(_folder.IsAppOwned(_root));
    }

    [Fact]
    public void IsAppOwned_FalseForAnUnrelatedSubfolder()
    {
        Assert.False(_folder.IsAppOwned(Path.Combine(_root, "2016 Holiday")));
    }

    [Fact]
    public void IsAppOwned_IgnoresTrailingSeparatorsAndCase()
    {
        Assert.True(_folder.IsAppOwned(_folder.ThumbnailsPath.ToUpperInvariant() + @"\"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
