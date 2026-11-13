using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// The folders on other computers that might be a folder just added here.
/// </summary>
/// <remarks>
/// Asked when a folder is added, before its first scan. That scan is the one
/// that matters: it takes the pictures another computer has already made, but
/// only for a folder this library knows is the same as one of theirs - and
/// until now the question was only put on the Sync screen, after a sync, which
/// on a new laptop was after the first scan had spent hours making everything
/// itself.
///
/// <para>Proposes and never decides, like the Sync screen: a person still
/// confirms, through <see cref="ConfirmPairingHandler"/>. A sync folder that is
/// not chosen, asleep or out of reach is answered with nothing to ask, under the
/// same deadline every other look at the folder has - adding a folder must not
/// wait on it.</para>
/// </remarks>
public sealed class FindSameFolderElsewhereHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionExchange _exchange;
    private readonly SyncFolderProbe _folder;

    public FindSameFolderElsewhereHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionExchange exchange,
        SyncFolderProbe folder)
    {
        _index = index;
        _decisions = decisions;
        _exchange = exchange;
        _folder = folder;
    }

    /// <param name="sharedSourceId">The shared identity of the folder just added.</param>
    public async Task<SameFolderElsewhere> HandleAsync(
        Guid sharedSourceId, CancellationToken cancellationToken = default)
    {
        SyncFolderAnswer folder =
            await _folder.AskAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        if (folder.Folder.Length == 0)
        {
            return SameFolderElsewhere.NoSyncFolder;
        }

        if (!folder.CanExchange)
        {
            return SameFolderElsewhere.Nothing;
        }

        MachineIdentity machine = await PublishDecisionsHandler
            .ThisMachineAsync(_index, cancellationToken)
            .ConfigureAwait(false);

        DecisionSet mine =
            await _decisions.ReadAsync(machine, cancellationToken).ConfigureAwait(false);

        SharedSource[] added = [.. mine.Sources.Where(source => source.SharedId == sharedSourceId)];

        if (added.Length == 0)
        {
            return SameFolderElsewhere.Nothing;
        }

        FetchedDecisions fetched =
            await _exchange.FetchAsync(cancellationToken).ConfigureAwait(false);

        // Every link anybody has published, not only this library's: a folder
        // another computer has already paired with a third is not news.
        SourceLink[] links = [.. mine.Links, .. fetched.Sets.SelectMany(set => set.Links)];

        // An identity another of this library's folders already has is not
        // offered. Taken, it would make two folders here one - the same share
        // added twice, once by a mapped drive - and every photograph's answers
        // would land on whichever copy happened to be read last.
        HashSet<Guid> taken =
        [
            .. mine.Sources.Select(source => source.SharedId).Where(id => id != sharedSourceId),
        ];

        return new SameFolderElsewhere(
            SyncFolderChosen: true,
            [
                .. fetched.Sets
                    .Where(them => them.Machine.Id != machine.Id)
                    .SelectMany(them => SourcePairing.Propose(
                        added, them.Sources, them.Machine.Name, links))
                    .Where(proposal => proposal.CanPair && !taken.Contains(proposal.Theirs.SharedId))
                    .OrderBy(proposal => proposal.Likeness),
            ]);
    }
}
