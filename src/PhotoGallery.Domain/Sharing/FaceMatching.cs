using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Finds the face another machine is talking about, among the ones this machine
/// found.
/// </summary>
/// <remarks>
/// An exact match is tried first, and between two machines running the detector
/// on the processor over the same rendition it is all that is needed: the same
/// graph draws the same rectangles.
///
/// <para>The fallback is for when that is not true, which is now the ordinary
/// case: the detector runs on the graphics card wherever there is one, and
/// floating point there moves a box by a pixel from one machine to the next. An
/// exact match then finds nothing and every answer in the library would be held
/// for a face sitting right there. So a box that overlaps a local one well
/// enough is that face, and anything below that is a face this machine does not
/// have, which is a different answer and gets a different outcome.</para>
///
/// <para>Taking another machine's faces asks the same question the same way -
/// see <see cref="VectorAcceptance.Landing"/> - so that a face skipped there as
/// already here is always one an answer can land on here.</para>
/// </remarks>
public static class FaceMatching
{
    /// <summary>
    /// How much two boxes must overlap before they are the same face.
    /// </summary>
    /// <remarks>
    /// Half, measured as intersection over union. Two detectors disagreeing about
    /// the same face differ by a pixel or two and score above 0.95; two genuinely
    /// different faces in one photograph do not overlap at all in the ordinary
    /// case and score 0. There is nothing near the middle to be careful about,
    /// which is why the exact figure matters less than having one - and half errs
    /// towards holding the answer, which is recoverable, over landing it on the
    /// wrong face, which is not.
    /// </remarks>
    public const double LeastOverlap = 0.5d;

    /// <summary>
    /// The box this machine holds for that face, or null when it has no such
    /// face - which means the answer waits rather than being lost.
    /// </summary>
    public static FaceBounds? Find(IReadOnlyList<FaceBounds> here, FaceBounds wanted)
    {
        ArgumentNullException.ThrowIfNull(here);

        FaceBounds? best = null;
        double bestOverlap = LeastOverlap;

        foreach (FaceBounds candidate in here)
        {
            if (candidate == wanted)
            {
                return candidate;
            }

            double overlap = Overlap(candidate, wanted);
            if (overlap > bestOverlap)
            {
                best = candidate;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    /// <summary>Intersection over union, from 1.0 for the same box down to 0.</summary>
    public static double Overlap(FaceBounds left, FaceBounds right)
    {
        int width = Span(left.X, left.Width, right.X, right.Width);
        int height = Span(left.Y, left.Height, right.Y, right.Height);

        if (width <= 0 || height <= 0)
        {
            return 0d;
        }

        long shared = (long)width * height;
        long union = (long)left.Area + right.Area - shared;

        return union <= 0 ? 0d : shared / (double)union;
    }

    /// <summary>How much two runs along one axis have in common.</summary>
    private static int Span(int leftFrom, int leftLength, int rightFrom, int rightLength) =>
        Math.Min(leftFrom + leftLength, rightFrom + rightLength) - Math.Max(leftFrom, rightFrom);
}
