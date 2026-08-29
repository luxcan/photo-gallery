using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.People;

/// <summary>Links a detected face to a named person.</summary>
public sealed class FaceAssignment
{
    public int Id { get; set; }

    public int FaceId { get; set; }

    public Face? Face { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public AssignmentSource Source { get; set; }

    /// <summary>Similarity that produced a proposal. Unset for manual assignments.</summary>
    public float? Score { get; set; }

    /// <summary>
    /// When this answer was given.
    /// </summary>
    /// <remarks>
    /// The app's own convention is <em>a date rather than a flag</em>, adopted so
    /// that decisions could be reviewed and undone, and this was the one human
    /// answer in the model with no date on it. A convention chosen for undo turns
    /// out to be exactly what merging two machines needs: of two answers about
    /// one face, the later one stands.
    ///
    /// <para><see cref="DateTime.MinValue"/> for rows made before this library
    /// could say, which is honest rather than tidy - those decisions happened,
    /// the moment was simply never recorded. It loses to any real date, which is
    /// the answer wanted at every point where the two differ.</para>
    ///
    /// <para>It does not settle everything, and must not be allowed to. A
    /// person's answer never loses to the app's guess whatever the clock says:
    /// clocks are the weak part of last-write-wins, close enough for two human
    /// answers minutes apart and not something to bet a confirmed name on against
    /// a proposal that happened to be written later.</para>
    /// </remarks>
    public DateTime DecidedUtc { get; set; }
}
