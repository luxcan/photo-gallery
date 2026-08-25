namespace PhotoGallery.Domain.Collections;

/// <summary>
/// What sort of occasion a collection is, which decides how it is named.
/// </summary>
/// <remarks>
/// A property of the row rather than of a formatting function that has to
/// guess. The clusterer knows whether the photographs were far from home and
/// how many days they cover; by the time a name is being written that is gone.
/// </remarks>
public enum CollectionKind
{
    /// <summary>
    /// A day out of a long ordinary stretch - photographs on consecutive days
    /// for longer than an occasion lasts.
    /// </summary>
    /// <remarks>
    /// Nought on purpose. Nought is what an unset column holds, and this is the
    /// only value that claims nothing: a row defaulting to <see cref="Trip"/>
    /// would assert a journey nobody measured.
    /// </remarks>
    Period = 0,

    /// <summary>One day, near where the photographs of that period usually are.</summary>
    Day = 1,

    /// <summary>Several consecutive days, but not far from home.</summary>
    Event = 2,

    /// <summary>
    /// Far enough from where that period's photographs usually are to be
    /// somewhere else.
    /// </summary>
    Trip = 3,
}
