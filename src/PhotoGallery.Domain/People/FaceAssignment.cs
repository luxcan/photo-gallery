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

    /// <summary>
    /// The machine that decided it, or empty for this library's own answer.
    /// </summary>
    /// <remarks>
    /// Empty rather than this machine's own id, so that nothing local has to
    /// remember to stamp it and a row written before this column existed is
    /// correctly attributed. It is resolved on the way out, where the machine
    /// publishing knows who it is.
    ///
    /// <para>It has to survive, because a machine publishes everything it holds
    /// rather than only what it decided itself. An answer that lost its author
    /// passing through would be republished as the forwarder's own and would
    /// start settling ties it has no business settling - and two machines that
    /// answered in the same second would then disagree about which of them won,
    /// depending on who had passed the answer on. Three laptops would never
    /// converge.</para>
    /// </remarks>
    public Guid DecidedBy { get; set; }
}
