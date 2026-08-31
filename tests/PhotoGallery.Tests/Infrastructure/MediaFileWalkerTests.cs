using System.Diagnostics;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Infrastructure;

public sealed class MediaFileWalkerTests : IDisposable
{
    private readonly string _root;
    private readonly string _photos;
    private readonly WorkingFolder _workingFolder;
    private readonly MediaFileWalker _walker;

    public MediaFileWalkerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-walk-{Guid.NewGuid():N}");
        _photos = Path.Combine(_root, "photos");
        Directory.CreateDirectory(_photos);
        _workingFolder = new WorkingFolder(Path.Combine(_root, "library"));
        _workingFolder.EnsureCreated();
        _walker = new MediaFileWalker(_workingFolder);
    }

    [Fact]
    public void Walk_SaysSoWhenTheRootCannotBeListed()
    {
        MediaWalk walk = _walker.Walk(Path.Combine(_root, "not-here"));

        Assert.True(walk.RootUnreadable);
        Assert.Empty(walk.Files);
    }

    [Fact]
    public void Walk_OfAnEmptyFolderIsNotTheSameAsAnUnreachableOne()
    {
        // The pair of these two tests is the whole point of returning a walk
        // rather than a bare sequence: both come back with no files, and the
        // caller deletes rows for one of them and not the other.
        MediaWalk walk = _walker.Walk(_photos);

        Assert.False(walk.RootUnreadable);
        Assert.Empty(walk.Files);
    }

    [Fact]
    public void Walk_RejectsABlankRootBeforeAnythingIsEnumerated()
    {
        // Without ever touching Files: the guard has to be eager, which it was
        // not while Walk was an iterator method.
        Assert.Throws<ArgumentException>(() => _walker.Walk(" "));
    }

    [Fact]
    public void Walk_FindsMediaAndReportsNothingUnreadable()
    {
        File.WriteAllText(Path.Combine(_photos, "a.jpg"), "x");
        Directory.CreateDirectory(Path.Combine(_photos, "2016"));
        File.WriteAllText(Path.Combine(_photos, "2016", "b.jpg"), "x");
        File.WriteAllText(Path.Combine(_photos, "notes.txt"), "x");

        MediaWalk walk = _walker.Walk(_photos);
        List<ScannedFile> files = [.. walk.Files];

        Assert.Equal(2, files.Count);
        Assert.Empty(walk.UnreadableFolders);
    }

    [Fact]
    public void Walk_DoesNotCallTheAppsOwnFoldersUnreadable()
    {
        // They are skipped by choice. Recording them as unreadable would protect
        // stale rows under them from ever being cleaned up.
        File.WriteAllText(Path.Combine(_workingFolder.ThumbnailsPath, "cached.jpg"), "x");
        File.WriteAllText(Path.Combine(_workingFolder.Root, "family.jpg"), "x");

        MediaWalk walk = _walker.Walk(_workingFolder.Root);
        List<ScannedFile> files = [.. walk.Files];

        Assert.Single(files);
        Assert.Empty(walk.UnreadableFolders);
    }

    [Fact]
    public void Walk_DoesNotFollowAJunctionBackToAnAncestor()
    {
        string child = Path.Combine(_photos, "child");
        string loop = Path.Combine(child, "back-to-photos");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "one.jpg"), "x");

        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(loop);
        start.ArgumentList.Add(_photos);

        using Process process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        try
        {
            MediaWalk walk = _walker.Walk(_photos);
            List<ScannedFile> files = [.. walk.Files];

            Assert.Single(files);
            Assert.Equal(Path.Combine("child", "one.jpg"), files[0].RelativePath);
        }
        finally
        {
            // Remove the junction itself before the fixture removes its target.
            Directory.Delete(loop);
        }
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
