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

    [Fact]
    public void AConfigLeftAtTheOldAddressIsAdoptedAndRemoved()
    {
        // It used to sit beside the executable. The first run at the new address
        // takes it over, so nobody has to re-choose a library they already chose.
        string older = Path.Combine(_tempRoot, "old-config.json");
        new JsonAppConfigStore(older).RememberFolder(_tempRoot);

        AppConfig adopted = new JsonAppConfigStore(_configPath, older).Load();

        Assert.Equal(_tempRoot, adopted.LastWorkingFolder);
        Assert.False(File.Exists(older), "the old file is moved, not copied");
    }

    [Fact]
    public void AdoptingIsAMigrationAndNotAFallback()
    {
        // The whole reason the old file is REMOVED rather than left behind.
        // Deleting config.json has to mean a clean start; if the old address were
        // read whenever the new one is missing, a deliberate delete would be
        // silently undone and the app would reopen a library nobody asked for.
        string older = Path.Combine(_tempRoot, "old-config.json");
        new JsonAppConfigStore(older).RememberFolder(_tempRoot);
        _ = new JsonAppConfigStore(_configPath, older).Load();

        File.Delete(_configPath);

        Assert.Null(new JsonAppConfigStore(_configPath, older).Load().LastWorkingFolder);
    }

    [Fact]
    public void AdoptingNeverOverwritesTheConfigAlreadyHere()
    {
        string older = Path.Combine(_tempRoot, "old-config.json");
        string current = Path.Combine(_tempRoot, "current");
        Directory.CreateDirectory(current);
        new JsonAppConfigStore(older).RememberFolder(_tempRoot);
        new JsonAppConfigStore(_configPath).RememberFolder(current);

        AppConfig kept = new JsonAppConfigStore(_configPath, older).Load();

        Assert.Equal(current, kept.LastWorkingFolder);
    }

    [Fact]
    public void TheDefaultAddressIsPerUserRatherThanBesideTheExecutable()
    {
        // Beside the exe is the folder publishing empties, which is how a failed
        // publish used to take the remembered library with it.
        string local = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        Assert.Equal(Path.Combine(local, "PhotoGallery"), JsonAppConfigStore.PerUserDirectory());
        Assert.StartsWith(local, JsonAppConfigStore.DefaultFilePath(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEnvironmentCanAimARunSomewhereElse()
    {
        // How a debug run is pointed at a throwaway library. It used to happen by
        // accident, because a build in bin\Debug had its own file beside its own
        // exe; now both share the per-user address unless this says otherwise.
        Assert.Equal(_tempRoot, JsonAppConfigStore.DirectoryFor(_tempRoot));
        Assert.Equal(JsonAppConfigStore.PerUserDirectory(), JsonAppConfigStore.DirectoryFor(null));
        Assert.Equal(JsonAppConfigStore.PerUserDirectory(), JsonAppConfigStore.DirectoryFor("   "));
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
