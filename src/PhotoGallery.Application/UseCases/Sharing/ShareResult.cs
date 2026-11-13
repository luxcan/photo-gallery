using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>What one share did: what it took, and what it gave.</summary>
public sealed record ShareResult(MergeResult Merged, PublishResult Published)
{
    /// <summary>
    /// Folders on another machine that might be folders here, to put to the
    /// user.
    /// </summary>
    /// <remarks>
    /// Only the ones somebody can act on. A pair filed at different depths is a
    /// diagnosis, and it is already in <see cref="Summary"/> - offering a button
    /// beside it would offer to make something worse.
    /// </remarks>
    public IReadOnlyList<PairingProposal> Offers =>
        [.. Merged.Pairings.Where(pairing => pairing.CanPair)];

    /// <summary>Whether anything happened at all.</summary>
    public bool Shared => Merged.Merged && Published.Published;

    /// <summary>
    /// What to put on screen.
    /// </summary>
    /// <remarks>
    /// The merge leads, because what arrived is what somebody pressed the button
    /// to find out. What was sent follows in a clause rather than a sentence of
    /// its own: it is reassurance that the other half happened, not news.
    /// </remarks>
    public string Summary
    {
        get
        {
            if (!Merged.Merged)
            {
                return Merged.Summary;
            }

            // Publishing failed after merging succeeded, which means the folder
            // went away between the two. Said plainly, because this computer's
            // changes will not reach anybody else until it syncs again. Why it
            // failed is for the log, not the screen.
            if (!Published.Published)
            {
                return (Merged.UpToDate, Merged.Changes) switch
                {
                    (true, string changes) =>
                        $"{changes}, but you are unable to sync your changes with others.",
                    (true, null) => "You are unable to sync your changes with others.",
                    _ => $"{Merged.Summary} You are unable to sync your changes with others.",
                };
            }

            return Merged.Summary;
        }
    }
}
