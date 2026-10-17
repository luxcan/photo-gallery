namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// What one sweep of the answers waiting for their photographs did.
/// </summary>
/// <remarks>
/// Four numbers rather than two, because "no longer waiting" and "applied" are
/// different claims. A sweep used to report everything it let go as applied, so
/// an answer that was already true here, or that a later answer had beaten, was
/// counted as work arriving when nothing had been written at all.
/// </remarks>
/// <param name="Applied">
/// Answers written this time, because the scan in front of this phase found the
/// photographs or the faces they had been waiting for.
/// </param>
/// <param name="Settled">
/// Answers let go without writing anything: what they said was already true
/// here, a later answer had beaten them, or the album or the person they named
/// had been deleted. Nothing is lost by letting these go - the answer that
/// stands is already in this library - but nothing arrived either.
/// </param>
/// <param name="Unplaced">
/// Moves kept waiting although their photograph is here, because the album they
/// name is not one this library can put it in yet. Kept rather than dropped: the
/// held row may be the only copy of the answer, and it comes good once a sync
/// places the photograph some other way.
/// </param>
/// <param name="Waiting">
/// Answers still waiting afterwards. Not a failure and not an error: most of
/// these are about photographs that are only on somebody else's laptop, and they
/// come good the day those pictures reach the shared folder. It is reported
/// because the alternative is a number that silently grows.
/// </param>
public sealed record HeldResult(
    int Applied, int Settled, int Unplaced, int Waiting, bool WasCancelled)
{
    public static HeldResult Nothing { get; } = new(0, 0, 0, 0, false);
}
