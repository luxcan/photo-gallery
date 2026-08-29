namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Which machines' face vectors may be believed, and which must be refused by
/// name.
/// </summary>
/// <remarks>
/// <strong>An embedding is meaningless outside the model that produced it, and a
/// mismatched one does not fail.</strong> It returns a confident answer about
/// the wrong person - which looks exactly like a right one, and would spread
/// through a family's library as fast as the sharing that carried it.
///
/// <para>So the check is a refusal rather than a warning, it is made before a
/// single vector is read, and what it refuses is only the vectors: the decisions
/// and the pictures from that machine are taken as usual, because neither
/// depends on a model at all.</para>
/// </remarks>
public static class VectorAcceptance
{
    /// <summary>
    /// Separates the vectors worth taking from the machines that have to be
    /// named.
    /// </summary>
    /// <param name="mine">
    /// This library's own models, by name and file digest. A model this machine
    /// does not have at all is not a mismatch: it has no vectors of its own to
    /// contradict, and taking somebody else's is the entire point.
    /// </param>
    public static (IReadOnlyList<FaceSet> Accepted, IReadOnlyList<ModelMismatch> Refused) Sift(
        IReadOnlyDictionary<string, string> mine, IReadOnlyList<FaceSet> theirs)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);

        List<FaceSet> accepted = [];
        List<ModelMismatch> refused = [];

        foreach (FaceSet them in theirs)
        {
            List<string> differing = [];

            foreach ((string model, string digest) in them.Models)
            {
                // Only where both machines have the file. A library that has
                // never installed the face models has nothing to disagree with,
                // and refusing on that basis would refuse exactly the machine
                // that most needs the transfer.
                if (mine.TryGetValue(model, out string? ours)
                    && !string.Equals(ours, digest, StringComparison.OrdinalIgnoreCase))
                {
                    differing.Add(model);
                }
            }

            if (differing.Count > 0)
            {
                refused.Add(new ModelMismatch(them.Machine, [.. differing.Order()]));
                continue;
            }

            accepted.Add(them);
        }

        return (accepted, refused);
    }

    /// <summary>
    /// The faces worth inserting: about photographs this library has, and about
    /// faces it has not already found.
    /// </summary>
    /// <param name="here">
    /// Photographs this library has indexed, and the boxes it has already found
    /// in each. A photograph absent from here is one whose faces are held with
    /// the answers, exactly as everything else about it is.
    /// </param>
    public static IReadOnlyList<SharedFace> Landing(
        IReadOnlyList<FaceSet> accepted, LibraryContents here)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(here);

        List<SharedFace> landing = [];
        HashSet<FaceKey> taken = [];

        // Already found here, by this machine's own pass or by an earlier
        // transfer. A second copy of one face would be a second person on the
        // same nose.
        foreach ((AssetKey photo, IReadOnlyList<Faces.FaceBounds> boxes) in here.Faces)
        {
            foreach (Faces.FaceBounds box in boxes)
            {
                taken.Add(new FaceKey(photo, box));
            }
        }

        foreach (FaceSet them in accepted)
        {
            foreach (SharedFace face in them.Faces)
            {
                if (!here.Photographs.Contains(face.Face.Photo) || !taken.Add(face.Face))
                {
                    continue;
                }

                landing.Add(face);
            }
        }

        return landing;
    }
}
