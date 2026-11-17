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

        // A computer somebody here said "not this one" to is not offered. One
        // nobody has answered for yet is, because saying its folder is the
        // same one is that answer: the question says this computer will take
        // its pictures and faces, and a yes records it - unless another
        // computer goes by its name, when the yes is about the folder alone.
        TrustedMachines trust =
            await TrustedMachines.ReadAsync(_decisions, cancellationToken).ConfigureAwait(false);

        // Every other computer's name, written down or only read just now, so
        // a question can say when another goes by the same one - which a copy
        // of a family laptop's file planted under a fresh id always does.
        IReadOnlyList<KnownMachine> known =
            await _decisions.KnownMachinesAsync(cancellationToken).ConfigureAwait(false);

        // The names somebody here gave them too: a file calling itself what
        // this computer already calls the kitchen laptop reads as that laptop.
        (Guid Id, string Name)[] names =
        [
            .. known.Select(other => (other.MachineId, other.Name)),
            .. known
                .Where(other => other.FamiliarName is not null)
                .Select(other => (other.MachineId, other.CalledHere)),
            .. fetched.Sets
                .Where(other => other.Machine.Id != machine.Id)
                .Select(other => (other.Machine.Id, other.Machine.Name)),
        ];

        bool SharesItsName(MachineIdentity them) =>
            names.Any(other => other.Id != them.Id
                && (KnownMachine.SameName(other.Name, them.Name)
                    || KnownMachine.SameName(other.Name, trust.CalledHere(them))));

        return new SameFolderElsewhere(
            SyncFolderChosen: true,
            [
                .. fetched.Sets
                    .Where(them => them.Machine.Id != machine.Id && !trust.Ignores(them.Machine.Id))
                    .SelectMany(them => SourcePairing
                        .Propose(added, them.Sources, them.Machine.Name, links)
                        .Select(proposal => proposal with
                        {
                            MachineId = them.Machine.Id,
                            SharesItsName = SharesItsName(them.Machine),
                            FamiliarName = trust.FamiliarName(them.Machine.Id),
                        }))
                    .Where(proposal => proposal.CanPair && !taken.Contains(proposal.Theirs.SharedId))
                    .OrderBy(proposal => proposal.Likeness),
            ]);
    }
}
