using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Infrastructure;

public sealed class JsonAppConfigStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _configPath;

    public JsonAppConfigStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"pg-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _configPath = Path.Combine(_tempRoot, "config.json");
    }

    private JsonAppConfigStore NewStore() => new(_configPath);

    [Fact]
    public void MissingFile_YieldsDefaults()
    {
        AppConfig config = NewStore().Load();

        Assert.Null(config.LastWorkingFolder);
    }

    [Fact]
    public void MissingFile_IsNotQuietlyRecoveredFromSomewhereElse()
    {
        // Deleting config.json has to mean a clean start. It only does if there
        // is no second copy anywhere for the app to fall back to.
        NewStore().RememberFolder(_tempRoot);
        File.Delete(_configPath);

        Assert.Null(NewStore().Load().LastWorkingFolder);
    }

    [Fact]
    public void ForgetLastFolder_MakesTheNextStartAskAgain()
    {
        JsonAppConfigStore store = NewStore();
        store.RememberFolder(_tempRoot);

        store.ForgetLastFolder();

        AppConfig config = NewStore().Load();
        Assert.Null(config.LastWorkingFolder);
    }

    [Fact]
    public void ForgetLastFolder_OnAFreshInstallIsHarmless()
    {
        NewStore().ForgetLastFolder();

        Assert.Null(NewStore().Load().LastWorkingFolder);
    }

    [Fact]
    public void CorruptFile_NeverStopsTheAppStarting()
    {
        File.WriteAllText(_configPath, "{ this is not json");

        AppConfig config = NewStore().Load();

        Assert.Equal(AppConfig.Empty, config);
    }

    [Fact]
    public void RememberFolder_SurvivesAReload()
    {
        NewStore().RememberFolder(_tempRoot);

        Assert.Equal(_tempRoot, NewStore().Load().LastWorkingFolder);
    }

    [Fact]
    public void RememberFolder_ReplacesThePreviousOne()
    {
        string second = Path.Combine(_tempRoot, "second");
        Directory.CreateDirectory(second);
        JsonAppConfigStore store = NewStore();

        store.RememberFolder(_tempRoot);
        store.RememberFolder(second);

        Assert.Equal(second, store.Load().LastWorkingFolder);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        NewStore().RememberFolder(_tempRoot);

        // The write goes via a .tmp then moves, so a crash cannot leave a
        // half-written config - but it must not leave the .tmp either.
        Assert.False(File.Exists(_configPath + ".tmp"));
        Assert.True(File.Exists(_configPath));
    }

    [Fact]
    public void TheFileHoldsNothingButTheFolder()
    {
        // The palette belongs to the library and is stored with it. A second
        // copy here could only ever disagree.
        NewStore().RememberFolder(_tempRoot);

        string written = File.ReadAllText(_configPath);
        Assert.Contains("LastWorkingFolder", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Theme", written, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent", written, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
