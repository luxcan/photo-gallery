namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// What one sweep of the answers waiting for their photographs did.
/// </summary>
/// <param name="Applied">
/// Answers that landed this time, because the scan in front of this phase found
/// the photographs or the faces they had been waiting for.
/// </param>
/// <param name="Waiting">
/// Answers still waiting afterwards. Not a failure and not an error: most of
/// these are about photographs that are only on somebody else's laptop, and they
/// come good the day those pictures reach the shared folder. It is reported
/// because the alternative is a number that silently grows.
/// </param>
public sealed record HeldResult(int Applied, int Waiting, bool WasCancelled)
{
    public static HeldResult Nothing { get; } = new(0, 0, false);
}
