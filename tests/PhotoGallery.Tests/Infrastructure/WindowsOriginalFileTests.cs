using PhotoGallery.Infrastructure.Storage;

namespace PhotoGallery.Tests.Infrastructure;

/// <summary>
/// The parts of deleting a user's photograph that can be checked without
/// putting anything in this machine's Recycle Bin.
/// </summary>
public sealed class WindowsOriginalFileTests
{
    [Fact]
    public void FlagsFor_AsksBeforeDestroyingAFileThatWasPromisedTheRecycleBin()
    {
        // Silent otherwise: a file too big for the bin, or on a drive whose bin
        // is switched off, would be deleted for good after the user was told it
        // could be got back.
        ushort flags = WindowsOriginalFile.FlagsFor(promisedRecycleBin: true);

        Assert.Equal(WindowsOriginalFile.FofWantNukeWarning, flags & WindowsOriginalFile.FofWantNukeWarning);
    }

    [Fact]
    public void FlagsFor_DoesNotAskAgainWhenTheUserWasToldItIsPermanent()
    {
        ushort flags = WindowsOriginalFile.FlagsFor(promisedRecycleBin: false);

        Assert.Equal(0, flags & WindowsOriginalFile.FofWantNukeWarning);
    }

    [Fact]
    public void Delete_CallsAFileThatIsAlreadyGoneDeleted()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"pg-gone-{Guid.NewGuid():N}.jpg");

        Assert.True(new WindowsOriginalFile().Delete(missing));
    }

    [Fact]
    public void Delete_RefusesABlankPath() =>
        Assert.False(new WindowsOriginalFile().Delete(" "));
}