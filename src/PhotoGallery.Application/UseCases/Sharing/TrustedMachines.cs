using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Which other computers this library takes changes from, as somebody here
/// said.
/// </summary>
/// <remarks>
/// <strong>Asked by everything that brings another computer's work into this
/// library</strong> - its answers, what its decode learned, its faces and the
/// pictures those name - and asked by id, which is the only thing a person's
/// answer was recorded against. A computer nobody here has answered for is
/// held back whole: one planted file in the sync folder could otherwise delete
/// every person and album here, and this library would then pass the deletions
/// on to every other laptop.
/// </remarks>
public sealed class TrustedMachines
{
    private readonly Dictionary<Guid, MachineTrust> _said = [];

    private TrustedMachines(IEnumerable<KnownMachine> known)
    {
        foreach (KnownMachine machine in known)
        {
            _said[machine.MachineId] = machine.Trust;
        }
    }

    public static async Task<TrustedMachines> ReadAsync(
        IDecisionReader decisions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        return new TrustedMachines(
            await decisions.KnownMachinesAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Whether somebody here said to take this computer's changes.</summary>
    public bool Takes(Guid machine) =>
        _said.TryGetValue(machine, out MachineTrust trust) && trust == MachineTrust.Trusted;

    /// <summary>Whether somebody here said "not this one".</summary>
    public bool Ignores(Guid machine) =>
        _said.TryGetValue(machine, out MachineTrust trust) && trust == MachineTrust.Ignored;
}
