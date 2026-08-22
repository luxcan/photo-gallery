using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.People;

/// <summary>
/// Decides who an unnamed face belongs to, out of everyone the user has named.
/// </summary>
/// <remarks>
/// A face is offered to whoever it looks most like, rather than to whoever asks
/// first. That distinction is the whole point of this type, and it was learned
/// the hard way: asking each person separately "does this clear my threshold?"
/// lets a weaker match claim a face while a far better one is standing right
/// there. Measured on a library of two siblings, 20.4% of confirmed faces could
/// be offered to the wrong person under the per-person rule and 0.3% under this
/// one - and the per-person rule's answer depended on the order the people
/// happened to be read in, which is not a fact about anybody's face.
///
/// <para>Siblings are what makes this necessary. Two unrelated people score
/// around 0.1 against each other; two children of the same parents, both
/// photographed as babies, score around 0.55. The model is not wrong to see the
/// resemblance - it is real - so the answer cannot be a higher threshold, which
/// would only lose the quieter matches. The answer is to compare the candidates
/// against each other.</para>
/// </remarks>
public static class FaceRouter
{
    /// <summary>
    /// How alike a face and a person's era must be before the face is theirs at
    /// all.
    /// </summary>
    /// <remarks>
    /// Measured on this library: two faces of different people score around 0.1
    /// and the same person across a year scores about 0.88. Half is comfortably
    /// between the two, and a proposal is a question rather than a claim, so
    /// erring towards asking costs a glance and erring the other way loses a
    /// photograph silently.
    /// </remarks>
    public const float MatchThreshold = 0.5f;

    /// <summary>
    /// How far ahead of the runner-up the best match must be to be offered at
    /// all.
    /// </summary>
    /// <remarks>
    /// Where two people are this close the app does not know, and saying so by
    /// staying quiet is better than guessing: measured on a real library, 31
    /// confirmed faces fall inside this margin and a quarter of them would be
    /// routed to the wrong person. They are few enough to lose and wrong often
    /// enough to be worth not asking about.
    ///
    /// <para>Set to zero to let the closest match always win.</para>
    /// </remarks>
    public const float Margin = 0.05f;

    /// <summary>
    /// The person this face most likely belongs to, or null when nobody is close
    /// enough or two people are too close to each other to separate.
    /// </summary>
    /// <param name="takenUtc">
    /// When the picture was taken. Each person is compared at the age they were
    /// then, which is why a child does not have to look like their own average.
    /// </param>
    /// <param name="isEligible">
    /// Whether a person may be considered at all - the caller's chance to drop
    /// somebody the user has already refused for this face. A refused person is
    /// not merely passed over: they leave the field entirely, so the face can
    /// still go to whoever was second.
    /// </param>
    public static RoutedFace? Route(
        DateTime takenUtc,
        FaceEmbedding embedding,
        IReadOnlyList<Person> candidates,
        Func<int, bool>? isEligible = null,
        float threshold = MatchThreshold,
        float margin = Margin)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (embedding.IsEmpty)
        {
            return null;
        }

        int bestId = 0;
        float best = float.NegativeInfinity;
        float runnerUp = float.NegativeInfinity;

        foreach (Person candidate in candidates)
        {
            if (isEligible is not null && !isEligible(candidate.Id))
            {
                continue;
            }

            PersonEra? era = candidate.EraFor(takenUtc);
            if (era is null || era.Centroid.IsEmpty)
            {
                continue;
            }

            float score = era.Centroid.SimilarityTo(embedding);
            if (score > best)
            {
                runnerUp = best;
                best = score;
                bestId = candidate.Id;
            }
            else if (score > runnerUp)
            {
                runnerUp = score;
            }
        }

        if (best < threshold)
        {
            return null;
        }

        // No runner-up means nobody to be confused with, so the margin has
        // nothing to say.
        return float.IsNegativeInfinity(runnerUp) || best - runnerUp >= margin
            ? new RoutedFace(bestId, best, runnerUp)
            : null;
    }
}
