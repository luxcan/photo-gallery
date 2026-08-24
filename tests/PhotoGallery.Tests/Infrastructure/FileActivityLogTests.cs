using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The log took over from the output panel, and it is now the only place a
/// failure the app chose not to interrupt the user for can still be read.
/// </summary>
public sealed class FileActivityLogTests : IDisposable
{
    private readonly string _root;
    private readonly WorkingFolder _workingFolder;

    public FileActivityLogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _workingFolder = new WorkingFolder(_root);
    }

    [Fact]
    public void Append_WritesTheLineIntoTheLogsFolder()
    {
        new FileActivityLog(_workingFolder).Append("refresh failed: the share went away");

        string[] files = Directory.GetFiles(_workingFolder.LogsPath);

        Assert.Single(files);
        Assert.Contains("refresh failed: the share went away", File.ReadAllText(files[0]));
    }

    [Fact]
    public void Append_CreatesTheLogsFolderWhenItIsNotThere()
    {
        // EnsureCreated normally makes it, but a working folder restored from a
        // backup - or one tidied by hand - must not lose its first failure.
        Assert.False(Directory.Exists(_workingFolder.LogsPath));

        new FileActivityLog(_workingFolder).Append("first line");

        Assert.True(Directory.Exists(_workingFolder.LogsPath));
    }

    [Fact]
    public void Append_AddsToTheDayRatherThanReplacingIt()
    {
        var log = new FileActivityLog(_workingFolder);

        log.Append("opened the library");
        log.Append("could not save the gallery order");

        string text = File.ReadAllText(Directory.GetFiles(_workingFolder.LogsPath)[0]);

        Assert.Contains("opened the library", text);
        Assert.Contains("could not save the gallery order", text);
    }

    [Fact]
    public void Append_DoesNotThrowWhenItCannotWrite()
    {
        // A file sitting where logs\ should be. The whole point of this log is to
        // carry failures, so one that threw would turn a handled failure into an
        // unhandled one - at exactly the moment something is already wrong.
        File.WriteAllText(_workingFolder.LogsPath, "not a directory");

        new FileActivityLog(_workingFolder).Append("anything at all");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that outlives the test run is not a test failure.
        }
    }
}
