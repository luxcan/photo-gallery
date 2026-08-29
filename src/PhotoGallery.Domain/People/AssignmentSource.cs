namespace PhotoGallery.Domain.People;

/// <summary>Where a face-to-person link came from, and how much to trust it.</summary>
public enum AssignmentSource
{
    /// <summary>The app matched it. Shown for confirmation, not treated as truth.</summary>
    Proposed = 0,

    /// <summary>You confirmed it. Only these feed an era's centroid.</summary>
    Confirmed = 1,

    /// <summary>You rejected it. Kept so the same wrong proposal is not made twice.</summary>
    Rejected = 2,

    /// <summary>
    /// You took the name off. The face is open again and nobody is suppressed.
    /// </summary>
    /// <remarks>
    /// Unnaming is its own answer rather than a rejection, and the row has to
    /// survive: a state-based merge cannot tell an absent row from one that was
    /// never there, so clearing a name by deleting the row would let the old name
    /// come straight back from the next machine.
    ///
    /// <para>Routing it through <see cref="Rejected"/> would borrow a meaning
    /// wider than it looks. A rejection suppresses that person for that face for
    /// good, so somebody who clears a name because they picked the wrong one, or
    /// simply wants to start again, would find that person could never be
    /// proposed there again - on every machine, after the merge.</para>
    /// </remarks>
    Cleared = 3,
}
