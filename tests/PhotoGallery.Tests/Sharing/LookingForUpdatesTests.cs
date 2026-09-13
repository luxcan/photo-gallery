using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// Asking whether there is anything to take, without taking it.
/// </summary>
/// <remarks>
/// The exchange stays something a person presses, because it writes to the
/// library. Knowing whether it is worth pressing should not also be a chore, so
/// this runs by itself - which means it has to be cheap, silent, and wrong only
/// in the direction that costs a press rather than the one that loses answers.
/// </remarks>
public sealed class LookingForUpdatesTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task NobodyHasPublished_SoThereIsNothingToTake()
    {
        Both(@"2019\a.jpg");

        SharedUpdate update = await Dad.Looking.HandleAsync();

        Assert.False(update.Any);
        Assert.Empty(update.Machines);
    }

    [Fact]
    public async Task SomebodyHasPublished_AndThisLibraryHasNeverTakenFromThem()
    {
        Both(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();

        SharedUpdate update = await Dad.Looking.HandleAsync();

        Assert.True(update.Any);

        // Described rather than named: the name is inside the file, and the
        // whole point of this check is that it does not open one.
        Assert.Contains("not taken answers from", Assert.Single(update.Machines));
        Assert.NotEqual(DateTime.MinValue, update.Newest);
    }

    [Fact]
    public async Task AfterTakingThem_ThereIsNothingToTakeAgain()
    {
        Both(@"2019\a.jpg");
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        SharedUpdate update = await Dad.Looking.HandleAsync();

        Assert.False(update.Any);
    }

    [Fact]
    public async Task AndSomethingAgainWhenTheyPublishAfterThat()
    {
        Both(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        // Named this time, because this library has taken from her before and
        // wrote down what she calls herself.
        await Mum.Publishing.HandleAsync();

        // Stamped rather than trusted. Publishing twice and merging in between
        // takes a millisecond here, and a file written twice inside one tick of
        // the clock can still report the first write - which is a question about
        // this test's speed and never about two laptops in a house, where the
        // gap is minutes. What is being pinned is the comparison.
        File.SetLastWriteTimeUtc(await MumsFileAsync(), DateTime.UtcNow.AddMinutes(1));

        SharedUpdate update = await Dad.Looking.HandleAsync();

        Assert.True(update.Any);
        Assert.Equal("Mum's laptop", Assert.Single(update.Machines));
    }

    [Fact]
    public async Task AMachineIsNeverNewsToItself()
    {
        // Publishing moves this library's own file every time, and a laptop that
        // told itself there was something waiting would say so after every share.
        Both(@"2019\a.jpg");
        await Dad.Publishing.HandleAsync();

        SharedUpdate update = await Dad.Looking.HandleAsync();

        Assert.False(update.Any);
    }

    [Fact]
    public async Task WithNoFolderChosen_ItAsksNothingAndSaysNothing()
    {
        var alone = new TwoLibraries();

        SharedUpdate update = await alone.Dad.Looking.HandleAsync();

        Assert.False(update.Any);

        alone.Dispose();
    }


    /// <summary>Where Mum's published answers sit, as a path on disk.</summary>
    private async Task<string> MumsFileAsync()
    {
        Guid hers = (await Mum.Index.GetSettingsAsync()).MachineId;

        return Path.Combine(_house.SharedFolder, "answers", $"{hers:D}.json.gz");
    }

    private void Both(string relativePath)
    {
        Mum.Face(Mum.Photo(relativePath), Head);
        Dad.Face(Dad.Photo(relativePath), Head);
    }

    public void Dispose() => _house.Dispose();
}
