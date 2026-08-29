using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Domain.Sharing.Direct;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Reaching another computer directly, either found on the network or typed by
/// hand.
/// </summary>
/// <remarks>
/// The second way to reach a machine, for the one that shares no folder with
/// anybody. The shared folder still wins on the deciding fact - it does not need
/// the other laptop switched on - so this is the way through rather than the way
/// everybody uses.
///
/// <para>The typed address matters more than it looks. A network set to Public
/// blocks the beacon, and guest Wi-Fi and access-point isolation block
/// machine-to-machine traffic outright with nothing this app can do about it.
/// In every one of those cases somebody reading an address off a screen is what
/// makes the feature work at all.</para>
/// </remarks>
public sealed class ReachMachineHandler
{
    private readonly ILibraryIndex _index;
    private readonly IPeerLink _link;
    private readonly IDecisionRepository _repository;

    public ReachMachineHandler(
        ILibraryIndex index, IPeerLink link, IDecisionRepository repository)
    {
        _index = index;
        _link = link;
        _repository = repository;
    }

    /// <summary>Asks who is at an address, without pairing with them.</summary>
    /// <remarks>
    /// Saying who you are is the one thing a machine will do unpaired, so this
    /// is what turns a typed address into a name somebody can recognise before
    /// they read six digits to it.
    /// </remarks>
    public async Task<ReachResult> FindAsync(
        string address, CancellationToken cancellationToken = default)
    {
        if (!Address.TryParse(address, out string host, out int port))
        {
            return ReachResult.CouldNot(
                "That does not look like a computer's name or address.");
        }

        MachineIdentity me = await PublishDecisionsHandler
            .ThisMachineAsync(_index, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PeerFound found = await _link
                .GreetAsync(host, port, me, cancellationToken)
                .ConfigureAwait(false);

            if (found.SchemaVersion > SharingVersion.Schema)
            {
                return ReachResult.CouldNot(
                    $"{found.Name} is running a newer version of Photo Gallery. Update this "
                  + "one and try again.");
            }

            return new ReachResult(true, string.Empty, host, port, found.Name, found.Fingerprint);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return ReachResult.CouldNot(
                $"Nothing answered at {host}. Check the address, and that Photo Gallery is "
              + "open on that computer.");
        }
    }

    /// <summary>
    /// Reads six digits to a machine and remembers it if they agree.
    /// </summary>
    /// <remarks>
    /// Afterwards the peer is remembered by fingerprint, and a fingerprint that
    /// changes means pairing again rather than a silent accept.
    /// </remarks>
    public async Task<ReachResult> PairAsync(
        string host, int port, string code, CancellationToken cancellationToken = default)
    {
        if (!PairingCode.IsWellFormed(code))
        {
            return ReachResult.CouldNot("A code is six digits.");
        }

        MachineIdentity me = await PublishDecisionsHandler
            .ThisMachineAsync(_index, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            PeerPairing outcome = await _link
                .PairAsync(host, port, me, code.Trim(), cancellationToken)
                .ConfigureAwait(false);

            if (!outcome.Succeeded)
            {
                return ReachResult.CouldNot(outcome.Problem);
            }

            await _repository
                .RememberAsync(
                    new MachineIdentity(
                        outcome.MachineId, outcome.Name, string.Empty, SharingVersion.Schema),
                    DateTime.UtcNow,
                    cancellationToken)
                .ConfigureAwait(false);

            return new ReachResult(
                true, string.Empty, host, port, outcome.Name, outcome.Fingerprint);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return ReachResult.CouldNot(
                "That computer stopped answering part-way through. Try again.");
        }
    }
}

/// <summary>What reaching a machine found, or why it could not.</summary>
public sealed record ReachResult(
    bool Reached, string Problem, string Host, int Port, string Name, string Fingerprint)
{
    public static ReachResult CouldNot(string problem) =>
        new(false, problem, string.Empty, 0, string.Empty, string.Empty);
}
