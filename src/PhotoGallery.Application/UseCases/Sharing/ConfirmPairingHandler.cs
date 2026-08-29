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

        MergePlan plan = MergePlan.Nothing with
        {
            Links = [link],
            Renames = SourcePairing.Adopt(
                [.. here.Sources.Select(source => source.SharedId)], every),
        };

        await _repository.ApplyAsync(plan, null, cancellationToken).ConfigureAwait(false);
    }
}
