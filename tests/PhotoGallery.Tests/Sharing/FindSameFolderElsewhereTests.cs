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
        Dad.Reaches(@"\\192.168.50.103\PhotoGallery");

        SameFolderElsewhere found = await OnDad.HandleAsync(Dad.SharedSourceId);

        Assert.False(found.SyncFolderChosen);
        Assert.Empty(found.Proposals);
    }

    [Fact]
    public async Task AFolderAnotherComputerKeepsUnderTheSamePath_IsPutToTheUser()
    {
        _house.Sharing();
        Mum.Reaches(@"\\192.168.50.103\PhotoGallery");
        Dad.Reaches(@"\\192.168.50.103\PhotoGallery");
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
        Mum.Reaches(@"\\192.168.50.103\PhotoGallery");
        Dad.Reaches(@"Z:\PhotoGallery");
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
        Mum.Reaches(@"\\192.168.50.103\PhotoGallery");
        Dad.Reaches(@"\\192.168.50.103\PhotoGallery\Photos");
        await Mum.Publishing.HandleAsync();

        SameFolderElsewhere found = await OnDad.HandleAsync(Dad.SharedSourceId);

        Assert.True(found.SyncFolderChosen);
        Assert.Empty(found.Proposals);
    }

    public void Dispose() => _house.Dispose();
}
