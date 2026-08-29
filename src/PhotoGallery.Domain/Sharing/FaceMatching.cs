using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Finds the face another machine is talking about, among the ones this machine
/// found.
/// </summary>
/// <remarks>
/// Normally this is an exact match and nothing more is needed: both machines ran
/// the same detector over the same rendition, and <c>OnnxFaceScanner</c> appends
/// no execution provider, so both ran the same CPU graph and drew the same
/// rectangles.
///
/// <para>The fallback is for the day that stops being true - a different model
/// version, or the GPU provider that is tempting to add and would move every box
/// by a pixel. Then an exact match finds nothing and every answer in the library
/// would be held for a face sitting right there. So a box that overlaps a local
/// one well enough is that face, and anything below that is a face this machine
/// does not have, which is a different answer and gets a different outcome.</para>
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
