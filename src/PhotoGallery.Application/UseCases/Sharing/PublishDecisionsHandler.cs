using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Writes everything this library has decided where the other machines in the
/// house can read it.
/// </summary>
/// <remarks>
/// One pass over decisions the index already holds: 469 KB written on this
/// library. It reads no original, needs no model, and touches the share only to
/// write one small file.
///
/// <para><strong>Everything it holds, not only what it decided itself.</strong>
/// Over a shared folder that changes nothing, because everybody reads
/// everybody's file. Over a direct connection it is the difference between
/// working and not: a laptop that only ever pairs with one other receives the
/// third machine's answers solely because that one's published set carries them.
/// Forwarding what you were told is what makes three machines converge with no
/// machinery for it.</para>
///
/// <para>The one thing left out is the app's own guesses. The other machine will
/// make its own, and better ones, from the confirmations it has just been given -
/// and sending a guess as though it were an answer is how one wrong proposal
/// becomes permanent across a whole family.</para>
/// </remarks>
public sealed class PublishDecisionsHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionExchange _exchange;

    public PublishDecisionsHandler(
        ILibraryIndex index, IDecisionReader decisions, IDecisionExchange exchange)
    {
        _index = index;
        _decisions = decisions;
        _exchange = exchange;
    }

    public async Task<PublishResult> HandleAsync(CancellationToken cancellationToken = default)
    {
        ExchangeReadiness readiness =
            await _exchange.ReadinessAsync(cancellationToken).ConfigureAwait(false);

        if (!readiness.CanExchange)
        {
            return PublishResult.CouldNot(readiness.Problem);
        }

        MachineIdentity machine =
            await ThisMachineAsync(_index, cancellationToken).ConfigureAwait(false);

        DecisionSet everything = await _decisions
            .ReadAsync(machine, cancellationToken)
            .ConfigureAwait(false);

        DecisionSet published = everything.WithoutProposals();

        await _exchange.PublishAsync(published, cancellationToken).ConfigureAwait(false);

        return new PublishResult(
            true,
            string.Empty,
            published.People.Count,
            published.Answers.Count + published.Strangers.Count,
            published.Albums.Count,
            published.WrittenUtc);
    }

    /// <summary>
    /// Who this library says it is, which every published answer is signed with.
    /// </summary>
    /// <remarks>
    /// Shared with the merge rather than built twice: the schema version decides
    /// whether another machine's payload can be read at all, and two places
    /// deciding it would eventually decide differently.
    /// </remarks>
    internal static async Task<MachineIdentity> ThisMachineAsync(
        ILibraryIndex index, CancellationToken cancellationToken)
    {
        LibrarySettings settings =
            await index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        return new MachineIdentity(
            settings.MachineId,
            settings.MachineName,
            SharingVersion.App,
            SharingVersion.Schema);
    }
}
