using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Gives another computer a name somebody here will recognise, in place of the
/// one it calls itself.
/// </summary>
/// <remarks>
/// Kept on this computer and nowhere else: nothing a sync reads or writes
/// touches it. That is the point of it - what the screen calls the laptop on
/// the kitchen table is then something a file in the sync folder cannot
/// choose.
/// </remarks>
public sealed class NameMachineHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _reader;
    private readonly IDecisionRepository _repository;

    public NameMachineHandler(
        ILibraryIndex index, IDecisionReader reader, IDecisionRepository repository)
    {
        _index = index;
        _reader = reader;
        _repository = repository;
    }

    /// <param name="machineId">The computer, by the id it gives itself.</param>
    /// <param name="typed">
    /// What somebody typed. Blank goes back to what the computer calls itself.
    /// </param>
    /// <returns>
    /// <see cref="MachineNaming.NameTaken"/>, and nothing written, where another
    /// computer already goes by that name here: two rows reading the same are
    /// what somebody answers for twice.
    /// </returns>
    public async Task<MachineNaming> HandleAsync(
        Guid machineId, string? typed, CancellationToken cancellationToken = default)
    {
        if (machineId == Guid.Empty)
        {
            throw new ArgumentException("A computer with no identity cannot be named.");
        }

        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (machineId == settings.MachineId)
        {
            throw new ArgumentException("This computer cannot be named as another one.");
        }

        IReadOnlyList<KnownMachine> known =
            await _reader.KnownMachinesAsync(cancellationToken).ConfigureAwait(false);

        KnownMachine machine = known.FirstOrDefault(row => row.MachineId == machineId)
            ?? throw new ArgumentException("That computer is not one this library knows.");

        string? familiar = KnownMachine.FamiliarNameFrom(typed, machine.Name);

        // Against every name another computer goes by here, its own as well as
        // one given it here - the rule the Sync screen and the same-folder
        // question call look-alikes by. Going back to its own name gives it no
        // new name, so it is never refused: that is how a clash a file made is
        // undone, not one this could add to.
        if (familiar is not null
            && known.Any(other => other.MachineId != machineId
                && other.NamesGoneBy().Any(name => KnownMachine.SameName(name, familiar))))
        {
            return MachineNaming.NameTaken;
        }

        if (!await _repository.NameAsync(machineId, familiar, cancellationToken).ConfigureAwait(false))
        {
            throw new ArgumentException("That computer is not one this library knows.");
        }

        return MachineNaming.Named;
    }
}
