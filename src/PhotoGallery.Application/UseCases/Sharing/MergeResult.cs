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

            // Said before the counts, and for the same reason as the line
            // above: a refusal is the question never having been asked, while
            // "Nothing new" is what a merge says when it asked properly and
            // there was nothing to hear. The two read identically on screen and
            // mean opposite things.
            //
            // The first exchange between any two libraries lands here, because
            // no two of them mint the same source identity until somebody says
            // their folders are one - so this is the sentence that has to carry
            // a person to that question, rather than leaving them at "Nothing
            // new" concluding the feature does not work.
            if (Outcome.Refused.Count > 0 && Outcome.ChangedNothing)
            {
                return Refusals();
            }

            List<string> parts = [];
            Add(parts, Outcome.NamesGained, "name", "names");
            Add(parts, Outcome.NamesReplaced, "answer replaced", "answers replaced");
            Add(parts, Outcome.PeopleGained, "person", "people");
            Add(parts, Outcome.FacesSetAside, "face set aside", "faces set aside");
            Add(parts, Outcome.PhotographsTurned, "photo turned", "photos turned");
            Add(parts, Outcome.AlbumsChanged, "album", "albums");
            Add(parts, Outcome.CollectionsChanged, "collection", "collections");
            Add(parts, Outcome.PhotographsMoved, "photo moved", "photos moved");

            string changed = parts.Count == 0
                ? "Nothing new"
                : string.Join(", ", parts);

            string said = Outcome.Held == 0
                ? changed + "."
                : $"{changed}. {Outcome.Held:N0} answers are waiting for photos this "
                  + "library has not indexed yet - scanning will bring them in.";

            // Part of the house heard from and part of it refused is the one
            // shape of this report that can mislead while every number in it is
            // right: the counts are true and incomplete at the same time.
            return Outcome.Refused.Count == 0 ? said : $"{said} {Refusals()}";
        }
    }

    /// <summary>
    /// The machines whose answers were not taken, and what to do about it.
    /// </summary>
    /// <remarks>
    /// Every refusal already carries a detail written to be read by a person
    /// rather than by a log, so these are joined rather than reworded.
    ///
    /// <para>A source not yet in common is the only one of the three reasons
    /// somebody can settle without leaving the screen, and it is the one every
    /// pair of libraries meets on its first exchange - so it is pointed at the
    /// question waiting below rather than merely stated. The other two, a newer
    /// release and a clock too far ahead, are answered somewhere else entirely
    /// and saying so here would be telling somebody to press something that is
    /// not on the screen.</para>
    /// </remarks>
    private string Refusals()
    {
        string said = string.Join(
            " ",
            Outcome.Refused.Select(refused =>
                $"Nothing was taken from {refused.Machine.Name} because {refused.Detail}."));

        bool answerable =
            Pairings.Count > 0
            && Outcome.Refused.Any(refused => refused.Reason == RefusalReason.NoSourceInCommon);

        return answerable
            ? said + " Say whether those two folders are the same one below, then share again."
            : said;
    }

    private static void Add(List<string> parts, int count, string one, string many)
    {
        if (count > 0)
        {
            parts.Add($"{count:N0} {(count == 1 ? one : many)}");
        }
    }
}
