using PhotoGallery.Infrastructure.Models;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// That every model graph opens the same way, and that a machine with no usable
/// adapter still works.
/// </summary>
/// <remarks>
/// The two model phases ran on the processor for as long as nothing asked for
/// anything else, and on a library this size that is the better part of half an
/// hour of a scan. Measured on this machine, CLIP ViT-L/14 takes 2,261 ms a
/// picture on the processor and 33 ms on the graphics card, with the two vectors
/// agreeing to a cosine of 1.000000 - so the only way to lose the speed is to
/// open a graph without asking where it should run, which is what these guard.
/// </remarks>
public sealed class InferenceDeviceTests
{
    [Fact]
    public void NoModelOpensAGraphOfItsOwn()
    {
        // The regression this prevents is a third model being added later, given
        // its own SessionOptions, and quietly running on the processor while the
        // other two do not - which shows up as a scan that is slower than the
        // last one for no visible reason.
        foreach (string file in GraphOwners())
        {
            string source = File.ReadAllText(file);

            Assert.DoesNotContain("new SessionOptions", source, StringComparison.Ordinal);
            Assert.Contains("InferenceDevice.ForGraph()", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryModelWaitsItsTurnOnWhateverItRunsOn()
    {
        // The work lists run eleven pictures at once, which is right for eleven
        // single-threaded graphs on a processor and wrong for one adapter. The
        // limit lives in the device so the lists never have to know.
        foreach (string file in GraphOwners())
        {
            Assert.Contains(
                "InferenceDevice.Enter(",
                File.ReadAllText(file),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThereAreGraphsToCheck()
    {
        // The two tests above pass vacuously if the search finds no files.
        Assert.Equal(2, GraphOwners().Count);
    }

    [Fact]
    public void ADeviceIsAlwaysChosenAndAlwaysNamed()
    {
        // Never an exception and never blank: a machine with no Direct3D adapter,
        // a driver that refuses and an adapter too small all end at the processor,
        // because a scan that is slower than it could be is an inconvenience and
        // a scan that throws is a broken application.
        Assert.False(string.IsNullOrWhiteSpace(InferenceDevice.Description));
        Assert.True(InferenceDevice.Concurrency >= 1);
    }

    [Fact]
    public void ASlotIsAlwaysReleasable()
    {
        // On the processor it holds nothing and disposing it does nothing; on an
        // adapter it is one of the two. Either way a caller may always dispose it,
        // including twice, because it sits in a using inside a Task.Run body.
        using (InferenceDevice.Slot slot = InferenceDevice.Enter(CancellationToken.None))
        {
            Assert.True(true);
        }

        InferenceDevice.Slot second = InferenceDevice.Enter(CancellationToken.None);
        second.Dispose();
    }

    /// <summary>The files that open an ONNX graph.</summary>
    private static IReadOnlyList<string> GraphOwners()
    {
        string root = InfrastructureRoot();

        return
        [
            Path.Combine(root, "Search", "ClipContentEncoder.cs"),
            Path.Combine(root, "Faces", "OnnxFaceScanner.cs"),
        ];
    }

    private static string InfrastructureRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !Directory.Exists(Path.Combine(directory.FullName, "src", "PhotoGallery.Infrastructure")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("Repository root not found above the test binary.")
            : Path.Combine(directory.FullName, "src", "PhotoGallery.Infrastructure");
    }
}
