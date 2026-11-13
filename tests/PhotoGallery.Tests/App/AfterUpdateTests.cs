using System.Diagnostics;
using PhotoGallery.App.Updates;

namespace PhotoGallery.Tests.App;

/// <summary>
/// A version started by the one it replaced waits for that one to close before
/// it opens the library.
/// </summary>
public sealed class AfterUpdateTests
{
    [Fact]
    public void TheProcessToWaitForIsReadFromTheArguments()
    {
        Assert.Equal(4242, AfterUpdate.PreviousProcess(["--after-update", "4242"]));
        Assert.Null(AfterUpdate.PreviousProcess([]));
        Assert.Null(AfterUpdate.PreviousProcess(["--after-update"]));
        Assert.Null(AfterUpdate.PreviousProcess(["--after-update", "-1"]));
    }

    [Fact]
    public void AnOrdinaryStartWaitsForNothing() =>
        Assert.False(AfterUpdate.WaitForPrevious([]));

    [Fact]
    public void ItWaitsUntilThePreviousVersionHasClosed()
    {
        using Process previous = Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 2 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;

        Assert.True(AfterUpdate.WaitForPrevious(["--after-update", previous.Id.ToString()]));
        Assert.True(previous.HasExited);
    }

    [Fact]
    public void APreviousVersionAlreadyGoneIsNotWaitedFor() =>
        Assert.True(AfterUpdate.WaitForPrevious(["--after-update", int.MaxValue.ToString()]));
}
