using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Sharing, as the one thing somebody actually wants to do.
/// </summary>
/// <remarks>
/// <strong>Taking and giving are one action, not two buttons.</strong> Nobody
/// wants to publish; they want everybody's answers to be everybody's answers.
/// Two buttons would be a procedure to remember, and the wrong order in that
/// procedure is not an error anything could report - it quietly works, and
/// leaves the house one merge behind for as long as nobody notices.
///
/// <para><strong>Merge first, then publish.</strong> That order is what makes
/// three machines converge with no machinery for it: this library's file carries
/// what it has just been told as well as what it decided itself, so a laptop
/// that only ever reads this one still receives everybody's answers. Publishing
/// first would write a set that is one merge out of date, every time.</para>
/// </remarks>
public sealed class ShareNowHandler
{
    private readonly MergeDecisionsHandler _merge;
    private readonly PublishDecisionsHandler _publish;

    public ShareNowHandler(MergeDecisionsHandler merge, PublishDecisionsHandler publish)
    {
        _merge = merge;
        _publish = publish;
    }

    public async Task<ShareResult> HandleAsync(
        IProgress<MergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        MergeResult merged = await _merge
            .HandleAsync(progress, cancellationToken)
            .ConfigureAwait(false);

        if (!merged.Merged)
        {
            // Nowhere to exchange, so there is nothing to publish either and the
            // reason is already the merge's. Reported once rather than twice.
            return new ShareResult(merged, PublishResult.CouldNot(merged.Summary));
        }

        // Published even when the merge changed nothing here, because "nothing
        // changed for me" and "nothing to say to anybody" are different: a
        // library that has just named forty faces of its own has plenty to say
        // and would take no answers doing it.
        PublishResult published =
            await _publish.HandleAsync(cancellationToken).ConfigureAwait(false);

        return new ShareResult(merged, published);
    }
}
