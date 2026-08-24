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
}
