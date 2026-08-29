using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>What a merge did, or why it could not run.</summary>
/// <param name="Machines">How many other machines had published anything.</param>
/// <param name="Unreadable">
/// Files that could not be understood. Named rather than swallowed, because a
/// smaller exchange reported as a complete one is the kind of quiet wrong this
/// feature cannot afford.
/// </param>
/// <param name="Pairings">
/// Folders on another machine that look like folders here, put to the user
/// rather than assumed - and the ones filed at different depths of the same
/// share, which are a mistake to report rather than a pair to offer.
/// </param>
public sealed record MergeResult(
    bool Merged,
    string Problem,
    MergeOutcome Outcome,
    int Machines,
    IReadOnlyList<UnreadableAnswers> Unreadable,
    IReadOnlyList<PairingProposal> Pairings)
{
    public static MergeResult CouldNot(string problem) =>
        new(false, problem, MergeOutcome.Nothing, 0, [], []);

    /// <summary>
    /// What to put on screen: what changed, by kind, or plainly that nothing did.
    /// </summary>
    /// <remarks>
    /// A merge that says nothing is a merge nobody can trust or undo, so this
    /// never answers with an empty string.
    /// </remarks>
    public string Summary
    {
        get
        {
            if (!Merged)
            {
                return Problem;
            }

            if (Machines == 0)
            {
                return "No other computer has shared anything yet.";
            }

            // Said before the counts, because an exchange that matched nothing
            // is not a small result - it is the wrong question having been
            // asked, and the counts below it would read as a complete answer.
            if (Pairings.FirstOrDefault(pairing =>
                    pairing.Likeness == PairingLikeness.FiledDifferently) is { } filed)
            {
                return $"Nothing matched. This library keeps its photos under "
                     + $"{filed.Mine.Root}, and {filed.MachineName} keeps the same pictures "
                     + $"under {filed.Theirs.Root} - so the two file them differently and no "
                     + "photo lines up. Point both at the same folder and share again.";
            }

            List<string> parts = [];
            Add(parts, Outcome.NamesGained, "name", "names");
            Add(parts, Outcome.NamesReplaced, "answer replaced", "answers replaced");
            Add(parts, Outcome.PeopleGained, "person", "people");
            Add(parts, Outcome.FacesSetAside, "face set aside", "faces set aside");
            Add(parts, Outcome.PhotographsTurned, "photo turned", "photos turned");
            Add(parts, Outcome.AlbumsChanged, "album", "albums");
            Add(parts, Outcome.PhotographsMoved, "photo moved", "photos moved");

            string changed = parts.Count == 0
                ? "Nothing new"
                : string.Join(", ", parts);

            return Outcome.Held == 0
                ? changed + "."
                : $"{changed}. {Outcome.Held:N0} answers are waiting for photos this "
                  + "library has not indexed yet - scanning will bring them in.";
        }
    }

    private static void Add(List<string> parts, int count, string one, string many)
    {
        if (count > 0)
        {
            parts.Add($"{count:N0} {(count == 1 ? one : many)}");
        }
    }
}
