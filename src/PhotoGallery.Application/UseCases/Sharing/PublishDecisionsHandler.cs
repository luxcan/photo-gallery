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
/// Over a shared folder that is redundant on the ordinary day, because
/// everybody reads everybody's file. It is kept because the merge is defined on
/// whole decision sets rather than on deltas, so a published set has to be
/// complete to be merged at all - and because a machine whose own file is lost
/// or deleted is then carried by everybody else's.</para>
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
    /// Shared rather than built twice: the schema version decides whether
    /// another machine's payload can be read at all, and two places deciding it
    /// would eventually decide differently.
    ///
    /// <para>Public because six handlers need it - scanning, merging, holding,
    /// pairing and both halves of sharing - and it belongs to none of them more
    /// than the others.</para>
    /// </remarks>
    public static async Task<MachineIdentity> ThisMachineAsync(
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
