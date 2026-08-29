using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.Ports;

/// <summary>What reading the other machines' answers produced.</summary>
/// <remarks>
/// The unreadable ones are carried rather than thrown, because one bad file must
/// not cost the exchange every good one - and must not pass silently either. A
/// machine writing its file at the moment this one reads the folder is the
/// ordinary case, and it comes good on the next run.
/// </remarks>
/// <param name="Unreadable">
/// Files that could not be understood, by name, with the reason. Named so the
/// screen can say which machine to look at rather than reporting a smaller
/// exchange than actually happened.
/// </param>
public sealed record FetchedDecisions(
    IReadOnlyList<DecisionSet> Sets,
    IReadOnlyList<UnreadableAnswers> Unreadable)
{
    public static FetchedDecisions None { get; } = new([], []);
}
