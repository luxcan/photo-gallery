using Microsoft.EntityFrameworkCore;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// What the Sharing screen opens showing, and what one press of its button does.
/// </summary>
public sealed class SharingStatusTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly FaceBounds Head = new(10, 10, 40, 40);

    private readonly TwoLibraries _house = new TwoLibraries().Sharing();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    [Fact]
    public async Task ItOpensSayingWhichFolderAndWhoHasShared()
    {
        Both(@"2019\a.jpg");
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        SharingStatus status = await Dad.Overview.HandleAsync();

        Assert.Equal(_house.SharedFolder, status.Folder);
        Assert.Empty(status.Problem);
        Assert.True(status.CanShare);

        MachineStanding mum = Assert.Single(status.Machines);
        Assert.Equal("Mum's laptop", mum.Name);
        Assert.True(mum.Merged);
        Assert.NotNull(mum.SharedUtc);
    }

    [Fact]
    public async Task AMachineThatHasSharedButNotBeenMergedFromIsStillListed()
    {
        // The ordinary state five seconds before somebody presses the button. A
        // screen that showed nothing here would be telling the user there is
        // nobody to share with, moments before they share with somebody.
        Both(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();

        SharingStatus status = await Dad.Overview.HandleAsync();

        MachineStanding waiting = Assert.Single(status.Machines);
        Assert.False(waiting.Merged);
        Assert.NotNull(waiting.SharedUtc);
        Assert.Contains("not taken answers from", waiting.Name);
    }

    [Fact]
    public async Task AMachineDoesNotAppearInItsOwnList()
    {
        Both(@"2019\a.jpg");
        await Mum.Publishing.HandleAsync();

        SharingStatus status = await Mum.Overview.HandleAsync();

        Assert.Empty(status.Machines);
    }

    [Fact]
    public async Task WithNoFolderChosenNothingIsReportedAsAProblem()
    {
        // The screen is about to ask for a folder. Saying "choose a folder"
        // beside the button that chooses one is telling somebody what they can
        // already see.
        using var alone = new TwoLibraries();

        SharingStatus status = await alone.Mum.Overview.HandleAsync();

        Assert.Empty(status.Folder);
        Assert.Empty(status.Problem);
        Assert.False(status.CanShare);
    }

    [Fact]
    public async Task AFolderThatCannotBeReachedSaysSoBeforeTheButtonIsPressed()
    {
        Both(@"2019\a.jpg");
        Dad.SharesThrough(Path.Combine(_house.SharedFolder, "a drive nobody plugged in"));

        SharingStatus status = await Dad.Overview.HandleAsync();

        Assert.False(status.CanShare);
        Assert.Contains("cannot be reached", status.Problem);
    }

    [Fact]
    public async Task ItSaysHowManyAnswersAreWaitingForPhotographs()
    {
        Mum.Answer(
            Mum.Face(Mum.Photo(@"2026 Phone Dump\b.jpg"), Head),
            Mum.Person("Ana"),
            AssignmentSource.Confirmed,
            Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Merging.HandleAsync();

        SharingStatus status = await Dad.Overview.HandleAsync();

        Assert.Equal(1, status.Waiting);
    }

    [Theory]
    [InlineData(0, "up to date")]
    [InlineData(5, "5 hours ago")]
    [InlineData(24, "1 day ago")]
    [InlineData(24 * 20, "2 weeks ago")]
    [InlineData(24 * 200, "6 months ago")]
    public void RecencyIsRoundedToWhatSomebodyActuallyWantsToKnow(int hoursAgo, string expected)
    {
        // Which laptop shared this morning, and which has been in a drawer since
        // spring. Nobody needs it to the hour.
        var machine = new MachineStanding("Dad's laptop", Monday.AddHours(-hoursAgo), true);

        Assert.Equal(expected, machine.Recency(Monday));
    }

    [Fact]
    public void AMachineThatHasSharedNothingSaysSo()
    {
        var machine = new MachineStanding("Ana's laptop", null, true);

        Assert.Equal("never shared", machine.Recency(Monday));
    }

    [Fact]
    public async Task SharingTakesAndGivesInOnePress()
    {
        // Nobody wants to publish. They want everybody's answers to be
        // everybody's answers, and two buttons would be a procedure to remember.
        Both(@"2019\a.jpg");
        Person ana = Mum.Person("Ana");
        Mum.Answer(Mum.Db.Faces.Single(), ana, AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();

        ShareResult shared = await Dad.Sharing.HandleAsync();

        Assert.True(shared.Shared);
        Assert.Contains("1 name", shared.Summary);
        Assert.Contains("Your answers are shared", shared.Summary);

        // Taken, and given back: Dad's own file now exists.
        Assert.Single(Dad.Db.FaceAssignments);
        Assert.True(shared.Published.Published);
    }

    [Fact]
    public async Task WhatOneMachineWasToldItPassesOnInTheSamePress()
    {
        // Merge before publish, which is what makes three machines converge with
        // no machinery for it. Publishing first would write a set one merge out
        // of date, every time.
        Both(@"2019\a.jpg");
        Mum.Answer(
            Mum.Db.Faces.Single(), Mum.Person("Ana"), AssignmentSource.Confirmed, Monday);

        await Mum.Publishing.HandleAsync();
        await Dad.Sharing.HandleAsync();

        // A third machine that has never heard from Mum reads only Dad's file.
        Library ana = _house.Add("Ana's laptop");
        ana.Face(ana.Photo(@"2019\a.jpg"), Head);

        await ana.Merging.HandleAsync();

        Assert.Equal(Mum.MachineId, ana.Db.FaceAssignments.Single().DecidedBy);
    }

    [Fact]
    public async Task WithNowhereToShareItSaysWhyAndPublishesNothing()
    {
        using var alone = new TwoLibraries();

        ShareResult shared = await alone.Mum.Sharing.HandleAsync();

        Assert.False(shared.Shared);
        Assert.Contains("Choose a folder", shared.Summary);
    }

    // ------------------------------------------------------------------ setup

    private void Both(string relativePath)
    {
        Mum.Face(Mum.Photo(relativePath), Head);
        Dad.Face(Dad.Photo(relativePath), Head);
    }

    public void Dispose() => _house.Dispose();
}
