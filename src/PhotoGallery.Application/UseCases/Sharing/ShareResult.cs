namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>What one share did: what it took, and what it gave.</summary>
public sealed record ShareResult(MergeResult Merged, PublishResult Published)
{
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
            // went away between the two. Said plainly, because this library has
            // taken answers nobody else will see until it is shared again.
            if (!Published.Published)
            {
                return $"{Merged.Summary} Your own answers could not be shared back: "
                     + Published.Problem;
            }

            return $"{Merged.Summary} Your answers are shared.";
        }
    }
}
