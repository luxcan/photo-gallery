using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Records that two folders, reached two ways, are the same folder.
/// </summary>
/// <remarks>
/// The one step in this feature that cannot be worked out. A UNC path and a
/// mapped drive letter are the same place and nothing in the text says so, and
/// absorbing two unrelated folders into one id would put every photograph in
/// each under a key meaning a different photograph in the other, with nothing
/// ever saying so. A person confirms, once.
///
/// <para>Applied here and published on the next share, like any other decision.
/// The rename is worked out from every link this library knows rather than from
/// this one, because three machines can pair pairwise and the third link is one
/// nobody ever confirmed.</para>
/// </remarks>
public sealed class ConfirmPairingHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionRepository _repository;

    public ConfirmPairingHandler(
        ILibraryIndex index, IDecisionReader decisions, IDecisionRepository repository)
    {
        _index = index;
        _decisions = decisions;
        _repository = repository;
    }

    public async Task HandleAsync(
        Guid mine, Guid theirs, CancellationToken cancellationToken = default)
    {
        if (mine == Guid.Empty || theirs == Guid.Empty)
        {
            throw new ArgumentException("A folder with no shared identity cannot be paired.");
        }

        if (mine == theirs)
        {
            // Already one folder. Not a fault, and nothing to write.
            return;
        }

        MachineIdentity machine = await PublishDecisionsHandler
            .ThisMachineAsync(_index, cancellationToken)
            .ConfigureAwait(false);

        DecisionSet here =
            await _decisions.ReadAsync(machine, cancellationToken).ConfigureAwait(false);

        SourceLink link = new SourceLink(mine, theirs, DateTime.UtcNow, machine.Id).Ordered();

        List<SourceLink> every = [.. here.Links, link];

        // Every identity the links touch, not only the ones this library owns.
        //
        // Asking about its own sources alone worked exactly half the time, and a
        // Guid decided which half: a pairing settles both folders on the lower of
        // the two ids, so when the other machine's id was the lower one this
        // library adopted it and its parked answers were carried across with the
        // source. When its own id was lower it kept it, no rename was emitted for
        // the other id at all, and every answer already parked under that id was
        // stranded - naming a folder that is now this folder, and released by no
        // scan and no later pairing.
        //
        // The other machine's id matches no row in PhotoSources here, so the
        // extra rename costs an update that changes nothing there and does the
        // whole job in HeldDecisions.
        HashSet<Guid> touched = [.. here.Sources.Select(source => source.SharedId)];

        foreach (SourceLink other in every)
        {
            touched.Add(other.Left);
            touched.Add(other.Right);
        }

        MergePlan plan = MergePlan.Nothing with
        {
            Links = [link],
            Renames = SourcePairing.Adopt(touched, every),
        };

        await _repository.ApplyAsync(plan, null, cancellationToken).ConfigureAwait(false);
    }
}
