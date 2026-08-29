namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>What publishing wrote, or why it could not.</summary>
/// <param name="Problem">
/// Plain language, ready to show. Empty when it worked. Every reason this fails
/// is one the user can do something about - a folder not chosen, a folder not
/// reachable - so none of them may be reported as a bare failure.
/// </param>
public sealed record PublishResult(
    bool Published,
    string Problem,
    int People,
    int Answers,
    int Albums,
    DateTime WrittenUtc)
{
    public static PublishResult CouldNot(string problem) =>
        new(false, problem, 0, 0, 0, DateTime.MinValue);
}
