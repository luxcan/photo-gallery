using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// The question a folder is asked when it is added: is it a folder another
/// computer already keeps?
/// </summary>
/// <remarks>
/// Asked then so the folder's first scan can take what the other computer has
/// already made. Found on a new laptop: the question was only ever put on the
/// Sync screen after a sync, by which time the first scan had spent hours
/// making every picture itself.
/// </remarks>
public sealed class FindSameFolderElsewhereTests : IDisposable
{
    private readonly TwoLibraries _house = new();

    private Library Mum => _house.Mum;

    private Library Dad => _house.Dad;

    private FindSameFolderElsewhereHandler OnDad =>
        new(Dad.Index, Dad.Decisions, Dad.Exchange, Dad.Probe);

    [Fact]
    public async Task WithNoSyncFolder_ItSaysSoAndAsksNothing()
    {
        Dad.Reaches(@"\\nas\Photos");

        SameFolderElsewhere found = await OnDad.HandleAsync(Dad.SharedSourceId);

        Assert.False(found.SyncFolderChosen);
        Assert.Empty(found.Proposals);
    }

    [Fact]
    public async Task AFolderAnotherComputerKeepsUnderTheSamePath_IsPutToTheUser()
    {
        _house.Sharing();
        Mum.Reaches(@"\\nas\Photos");
        Dad.Reaches(@"\\nas\Photos");
        await Mum.Publishing.HandleAsync();

        SameFolderElsewhere found = await OnDad.HandleAsync(Dad.SharedSourceId);

        Assert.True(found.SyncFolderChosen);
        PairingProposal proposal = Assert.Single(found.Proposals);
        Assert.Equal("Mum's laptop", proposal.MachineName);
        Assert.Equal(Dad.SharedSourceId, proposal.Mine.SharedId);
        Assert.Equal(Mum.SharedSourceId, proposal.Theirs.SharedId);
    }

    [Fact]
    public async Task OnceConfirmed_TheFolderIsNotAskedAboutAgain()
    {
        _house.Sharing();
        Mum.Reaches(@"\\nas\Photos");
        Dad.Reaches(@"Z:\Photos");
        await Mum.Publishing.HandleAsync();

        PairingProposal proposal =
            Assert.Single((await OnDad.HandleAsync(Dad.SharedSourceId)).Proposals);
        await Dad.Pairing.HandleAsync(proposal.Mine.SharedId, proposal.Theirs.SharedId);

        Guid adopted = Dad.Db.PhotoSources.Single(source => source.Id == 1).SharedId;
        SameFolderElsewhere again = await OnDad.HandleAsync(adopted);

        Assert.Empty(again.Proposals);
    }

    [Fact]
    public async Task FoldersFiledAtDifferentDepths_AreNotOfferedAsOne()
    {
        // Pairing those would leave every path below them differing by a
        // prefix, so the Sync screen reports it rather than offering it - and so
        // must this.
        _house.Sharing();
        Mum.Reaches(@"\\nas\Photos");
        Dad.Reaches(@"\\nas\Photos\Family");
        await Mum.Publishing.HandleAsync();

        SameFolderElsewhere found = await OnDad.HandleAsync(Dad.SharedSourceId);

        Assert.True(found.SyncFolderChosen);
        Assert.Empty(found.Proposals);
    }

    [Fact]
    public async Task AFolderThisLibraryAlreadyHasUnderThatIdentity_IsNotOfferedAgain()
    {
        // Dad already has the share, paired with Mum's; now he adds it a second
        // time by a mapped drive. Taking Mum's identity again would make his two
        // folders one.
        _house.Sharing();
        Mum.Reaches(@"\\nas\Photos");
        await Mum.Publishing.HandleAsync();
        Dad.Folder(@"\\nas\Photos", Mum.SharedSourceId);
        PhotoGallery.Domain.Library.PhotoSource mapped = Dad.Folder(@"Z:\Photos");

        SameFolderElsewhere found = await OnDad.HandleAsync(mapped.SharedId);

        Assert.Empty(found.Proposals);
    }

    [Fact]
    public async Task AComputerNamedHere_IsAskedAboutByThatName()
    {
        await MumKeepsTheShareDadIsAddingAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        PairingProposal proposal =
            Assert.Single((await OnDad.HandleAsync(Dad.SharedSourceId)).Proposals);

        Assert.Equal(Mum.MachineId, proposal.MachineId);
        Assert.Equal("Mum's laptop", proposal.MachineName);
        Assert.Equal("Kitchen laptop", proposal.FamiliarName);
        Assert.Equal("Kitchen laptop", proposal.CalledHere);
        Assert.False(proposal.SharesItsName);
    }

    /// <summary>
    /// A file calling itself what this computer calls one of the house's
    /// laptops - by the name given it here, or by its own - is a look-alike,
    /// so a yes to the folder is not a yes to either computer.
    /// </summary>
    [Theory]
    [InlineData("Kitchen laptop")]
    [InlineData("kitchen LAPTOP")]
    [InlineData("Mum's laptop")]
    public async Task AFileCallingItselfANameMumGoesByHere_IsALookAlike(string plantedName)
    {
        await MumKeepsTheShareDadIsAddingAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        Library planted = _house.Stranger(plantedName);
        planted.Reaches(@"\\nas\Photos");
        await planted.Publishing.HandleAsync();

        SameFolderElsewhere found = await OnDad.HandleAsync(Dad.SharedSourceId);

        PairingProposal copy = Assert.Single(
            found.Proposals, proposal => proposal.MachineId == planted.MachineId);
        Assert.True(copy.SharesItsName);
        Assert.Null(copy.FamiliarName);

        PairingProposal hers = Assert.Single(
            found.Proposals, proposal => proposal.MachineId == Mum.MachineId);
        Assert.True(hers.SharesItsName);
        Assert.Equal("Kitchen laptop", hers.CalledHere);
    }

    [Fact]
    public async Task AnotherComputerWithANameOfItsOwn_IsNoLookAlike()
    {
        await MumKeepsTheShareDadIsAddingAsync();
        await Dad.Naming.HandleAsync(Mum.MachineId, "Kitchen laptop");

        Library spare = _house.Stranger("LAPTOP-AB12CD");
        spare.Reaches(@"\\nas\Photos");
        await spare.Publishing.HandleAsync();

        SameFolderElsewhere found = await OnDad.HandleAsync(Dad.SharedSourceId);

        Assert.Equal(2, found.Proposals.Count);
        Assert.All(found.Proposals, proposal => Assert.False(proposal.SharesItsName));
    }

    /// <summary>
    /// Mum, trusted on Dad's laptop, has published the share Dad is adding
    /// under the same path.
    /// </summary>
    private async Task MumKeepsTheShareDadIsAddingAsync()
    {
        _house.Sharing();
        Mum.Reaches(@"\\nas\Photos");
        Dad.Reaches(@"\\nas\Photos");
        await Mum.Publishing.HandleAsync();
    }

    public void Dispose() => _house.Dispose();
}
