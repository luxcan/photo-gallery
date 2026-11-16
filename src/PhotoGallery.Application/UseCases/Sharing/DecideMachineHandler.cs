using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Records whether to take another computer's changes, as somebody here
/// answered.
/// </summary>
/// <remarks>
/// The one fact about another computer that no file in the sync folder can
/// supply, so it is asked of a person once - as whether two folders are one is
/// - and remembered. Either answer can be given again later the other way: a
/// "not this one" said in haste about a laptop that turns out to be the
/// family's own must not be the end of that laptop.
///
/// <para>Recorded, not acted on. The changes arrive on the next Sync, which
/// reads the folder as it is then rather than as it was when the question was
/// put.</para>
/// </remarks>
public sealed class DecideMachineHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionRepository _repository;

    public DecideMachineHandler(ILibraryIndex index, IDecisionRepository repository)
    {
        _index = index;
        _repository = repository;
    }

    /// <param name="machineId">The computer, by the id it gives itself.</param>
    /// <param name="name">
    /// What it calls itself, kept where it was not met before. Not checked: it
    /// came from the computer's own file, and a file with no name in it must
    /// still be a computer somebody can say "not this one" to.
    /// </param>
    /// <param name="take">True to take its changes, false for "not this one".</param>
    public async Task HandleAsync(
        Guid machineId, string? name, bool take, CancellationToken cancellationToken = default)
    {
        if (machineId == Guid.Empty)
        {
            throw new ArgumentException("A computer with no identity cannot be answered for.");
        }

        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (machineId == settings.MachineId)
        {
            // A file claiming to be this computer. Never something to trust or
            // to refuse: this computer's own work is not taken from a file.
            throw new ArgumentException("This computer cannot be answered for as another one.");
        }

        await _repository
            .DecideAsync(
                machineId,
                name,
                take ? MachineTrust.Trusted : MachineTrust.Ignored,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
