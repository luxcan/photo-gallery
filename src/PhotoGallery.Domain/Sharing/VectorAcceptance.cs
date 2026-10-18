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
    /// <remarks>
    /// <strong>Already found means what the answers mean by it.</strong> A face is
    /// here when <see cref="FaceMatching.Find"/> finds it here - the exact box,
    /// or one overlapping it by more than half - which is the rule a waiting name
    /// uses to find the face it is about. The detector runs on the graphics card,
    /// whose floating point moves a box by a pixel from one machine to the next,
    /// so judged by the exact box alone every face two machines both found would
    /// arrive as a second face on the same nose. And judged by any other line than
    /// the answers', a face at the boundary would be skipped as already here while
    /// the name meant for it was refused as not here, and waited for ever.
    ///
    /// <para>Each face that lands joins the faces here before the next is weighed,
    /// so a third machine's copy of the same face, a pixel off again, is judged
    /// against it rather than landing beside it in the same run.</para>
    ///
    /// <para><strong>And only on the picture it was drawn on.</strong> A face
    /// travels with the path of its photograph and nothing to say which version
    /// of the file it was found in, and a box on a cropped picture is in the
    /// wrong place on the uncropped one. What does say is the facts the same
    /// machine left: the rendition it had, named after the bytes it was made
    /// from. So a face lands where this library has that same picture, on its
    /// disk, and nowhere else - which also leaves alone a photograph whose
    /// picture has not arrived here, whose face pass will draw its own the
    /// moment it has, and replace anything taken before.</para>
    ///
    /// <para>And only from faces written after that version of the file was. A
    /// machine's facts are written on every scan and its faces only when they
    /// change - not at all while another computer is on the build before - so
    /// the faces in the folder can be older than the facts beside them: boxes
    /// left before the file was cropped, beside facts that already name the
    /// picture of the crop. A face cannot be about a file that did not exist yet
    /// when it was written.</para>
    ///
    /// <para>Nor onto a photograph whose own faces are waiting to be found again.
    /// Taking marks the photograph looked at, and that would stop the face pass
    /// that is about to replace them.</para>
    /// </remarks>
    /// <param name="here">
    /// Photographs this library has indexed, the boxes it has already found in
    /// each, and the picture each has on this disk. A photograph absent from
    /// here is one whose faces are held with the answers, exactly as everything
    /// else about it is.
    /// </param>
    /// <param name="drawnOn">
    /// The facts the machines giving these faces left beside them, which say
    /// which picture each face was drawn on. A machine with none here has said
    /// nothing about its pictures, and its faces wait until it has.
    /// </param>
    public static IReadOnlyList<SharedFace> Landing(
        IReadOnlyList<FaceSet> accepted,
        LibraryContents here,
        IReadOnlyList<PreparedSet> drawnOn)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(here);
        ArgumentNullException.ThrowIfNull(drawnOn);

        Dictionary<Guid, Dictionary<AssetKey, PreparedFact>> pictures = [];

        foreach (PreparedSet them in drawnOn)
        {
            Dictionary<AssetKey, PreparedFact> theirs = [];

            foreach (PreparedFact fact in them.Facts.Where(fact => fact.HasPicture))
            {
                theirs[fact.Photo] = fact;
            }

            pictures[them.Machine.Id] = theirs;
        }

        List<SharedFace> landing = [];

        // Already found here, by this machine's own pass or by an earlier
        // transfer. A second copy of one face would be a second person on the
        // same nose. Copied, because the faces landing join them as they land.
        Dictionary<AssetKey, List<Faces.FaceBounds>> found = [];

        foreach ((AssetKey photo, IReadOnlyList<Faces.FaceBounds> boxes) in here.Faces)
        {
            found[photo] = [.. boxes];
        }

        foreach (FaceSet them in accepted)
        {
            if (!pictures.TryGetValue(them.Machine.Id, out Dictionary<AssetKey, PreparedFact>? theirs))
            {
                continue;
            }

            foreach (SharedFace face in them.Faces)
            {
                AssetKey photo = face.Face.Photo;

                if (!theirs.TryGetValue(photo, out PreparedFact? drawn)
                    || !OnTheSamePicture(photo, here, drawn, them.WrittenUtc))
                {
                    continue;
                }

                if (!found.TryGetValue(photo, out List<Faces.FaceBounds>? boxes))
                {
                    found[photo] = boxes = [];
                }

                if (FaceMatching.Find(boxes, face.Face.Bounds) is not null)
                {
                    continue;
                }

                boxes.Add(face.Face.Bounds);
                landing.Add(face);
            }
        }

        return landing;
    }

    /// <summary>
    /// Whether a face another machine drew on a photograph sits on the picture
    /// this library has of it, and may be added to it now.
    /// </summary>
    /// <param name="drawn">What the giving machine's facts say about the photograph.</param>
    /// <param name="writtenUtc">When the giving machine wrote its faces.</param>
    private static bool OnTheSamePicture(
        AssetKey photo, LibraryContents here, PreparedFact drawn, DateTime writtenUtc)
    {
        // A photograph turned here has its boxes in the turned frame, and this
        // one was drawn on a picture nobody had turned. One whose own faces are
        // out of date is about to be looked at again, and taking would stop it.
        if (!here.Photographs.Contains(photo)
            || here.Turned.Contains(photo)
            || here.Outdated.Contains(photo))
        {
            return false;
        }

        return here.Renditions.TryGetValue(photo, out string? mine)
            && string.Equals(mine, drawn.ThumbnailName, StringComparison.OrdinalIgnoreCase)
            && drawn.ModifiedUtc <= writtenUtc;
    }
}
