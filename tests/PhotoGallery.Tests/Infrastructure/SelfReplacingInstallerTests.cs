using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using PhotoGallery.Application.Ports;
using PhotoGallery.Infrastructure.Updates;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// Fetching a new version, proving it, and putting it in place of the running
/// one - with every way that can fail leaving the old version whole.
/// </summary>
public sealed class SelfReplacingInstallerTests : IDisposable
{
    private static readonly byte[] NewBuild = Encoding.ASCII.GetBytes("the new build");
    private static readonly byte[] OldBuild = Encoding.ASCII.GetBytes("the build that is running");

    private readonly string _root;
    private readonly string _install;
    private readonly string _staging;
    private readonly string _running;

    public SelfReplacingInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pg-update-{Guid.NewGuid():N}");
        _install = Path.Combine(_root, "install");
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_install);

        _running = Path.Combine(_install, "PhotoGallery.exe");
        File.WriteAllBytes(_running, OldBuild);
    }

    [Fact]
    public async Task AProvedDownloadIsStagedOutsideTheInstallFolder()
    {
        var progress = new Recorded();

        string staged = await Installer(Serving(NewBuild))
            .DownloadAsync(Release(Sha(NewBuild)), progress);

        Assert.Equal(NewBuild, await File.ReadAllBytesAsync(staged));
        Assert.StartsWith(_staging, staged, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1d, progress.Last);
        Assert.Single(Directory.GetFiles(_staging));
    }

    [Fact]
    public async Task ADownloadThatDoesNotMatchItsDigestIsThrownAway()
    {
        UpdateFailedException failed = await Assert.ThrowsAsync<UpdateFailedException>(() =>
            Installer(Serving(NewBuild)).DownloadAsync(Release(Sha(OldBuild))));

        Assert.Contains("damaged", failed.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_staging));
    }

    [Fact]
    public async Task TheDigestCanComeFromTheFileBesideTheExecutable()
    {
        var sidecar = new Uri("https://example.com/PhotoGallery.exe.sha256");
        var served = new Dictionary<string, byte[]>
        {
            ["/PhotoGallery.exe"] = NewBuild,
            ["/PhotoGallery.exe.sha256"] = Encoding.ASCII.GetBytes($"{Sha(NewBuild)}  PhotoGallery.exe"),
        };

        string staged = await Installer(new FakeServer(served))
            .DownloadAsync(Release(null) with { Sha256Address = sidecar });

        Assert.Equal(NewBuild, await File.ReadAllBytesAsync(staged));
    }

    [Fact]
    public async Task AReleaseThatCannotBeProvedIsNotDownloaded()
    {
        await Assert.ThrowsAsync<UpdateFailedException>(() =>
            Installer(Serving(NewBuild)).DownloadAsync(Release(null)));
    }

    [Fact]
    public async Task NoConnection_SaysSoInASentence()
    {
        UpdateFailedException failed = await Assert.ThrowsAsync<UpdateFailedException>(() =>
            Installer(new FakeServer([])).DownloadAsync(Release(Sha(NewBuild))));

        Assert.Contains("internet connection", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSwapPutsTheNewBuildInPlaceAndKeepsTheOldAside()
    {
        string staged = Staged(NewBuild);

        SelfReplacingInstaller.Replace(staged, _running, _running + ".new", Old);

        Assert.Equal(NewBuild, File.ReadAllBytes(_running));
        Assert.Equal(OldBuild, File.ReadAllBytes(Old));
        Assert.False(File.Exists(_running + ".new"));
    }

    [Fact]
    public void ARunningProgramCanBeSwappedOut()
    {
        // The whole design rests on this: Windows lets a running executable's
        // file be renamed, though not overwritten. Proved on a real process
        // rather than assumed.
        string program = Path.Combine(_install, "PhotoGallery.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), program, overwrite: true);

        using Process running = Process.Start(new ProcessStartInfo(program, "-n 30 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;

        try
        {
            SelfReplacingInstaller.Replace(Staged(NewBuild), program, program + ".new", Old);

            Assert.Equal(NewBuild, File.ReadAllBytes(program));
            Assert.True(File.Exists(Old));
            Assert.False(running.HasExited);
        }
        finally
        {
            running.Kill();
            running.WaitForExit();
        }
    }

    [Fact]
    public void AFileSomebodyElseHoldsIsLeftExactlyAsItWas()
    {
        // What another computer running it from the same NAS folder looks like:
        // a handle that does not allow the file to be renamed.
        using (new FileStream(_running, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            UpdateFailedException failed = Assert.Throws<UpdateFailedException>(() =>
                SelfReplacingInstaller.Replace(Staged(NewBuild), _running, _running + ".new", Old));

            Assert.Contains("another computer", failed.Message, StringComparison.Ordinal);
        }

        Assert.Equal(OldBuild, File.ReadAllBytes(_running));
        Assert.False(File.Exists(_running + ".new"));
        Assert.False(File.Exists(Old));
    }

    [Fact]
    public void APreviousVersionStillLyingAroundIsReplaced()
    {
        File.WriteAllText(Old, "an update ago");

        SelfReplacingInstaller.Replace(Staged(NewBuild), _running, _running + ".new", Old);

        Assert.Equal(OldBuild, File.ReadAllBytes(Old));
    }

    [Fact]
    public void CleanUpRemovesWhatAnUpdateLeavesBehind()
    {
        File.WriteAllText(Old, "previous");
        File.WriteAllText(_running + ".new", "half");
        string staged = Staged(NewBuild);

        Installer(Serving(NewBuild)).CleanUp();

        Assert.False(File.Exists(Old));
        Assert.False(File.Exists(_running + ".new"));
        Assert.False(File.Exists(staged));
        Assert.True(File.Exists(_running));
    }

    [Fact]
    public void ABuildFromSource_CannotReplaceItself()
    {
        Assert.True(Installer(Serving(NewBuild)).CanReplaceItself);

        // dotnet run leaves the assembly beside its app host; a published
        // single file has it inside.
        File.WriteAllText(Path.ChangeExtension(_running, ".dll"), string.Empty);

        Assert.False(Installer(Serving(NewBuild)).CanReplaceItself);
    }

    private string Old => SelfReplacingInstaller.OldPath(_running);

    private SelfReplacingInstaller Installer(HttpMessageHandler server) =>
        new(new HttpClient(server), _running, _staging);

    private static FakeServer Serving(byte[] build) =>
        new(new Dictionary<string, byte[]> { ["/PhotoGallery.exe"] = build });

    private static ReleaseDownload Release(string? sha256) =>
        new(new Uri("https://example.com/PhotoGallery.exe"), NewBuild.Length, sha256, null);

    private string Staged(byte[] build)
    {
        Directory.CreateDirectory(_staging);
        string path = Path.Combine(_staging, "PhotoGallery-staged.exe");
        File.WriteAllBytes(path, build);
        return path;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder; a killed process may hold its file a moment.
        }
    }

    /// <summary>Answers from a table of paths; anything else is a dead line.</summary>
    private sealed class FakeServer(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!files.TryGetValue(request.RequestUri!.AbsolutePath, out byte[]? body))
            {
                throw new HttpRequestException("no route to host");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }

    private sealed class Recorded : IProgress<double>
    {
        public double Last { get; private set; }

        public void Report(double value) => Last = value;
    }
}
