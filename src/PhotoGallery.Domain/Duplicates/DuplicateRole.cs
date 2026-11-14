namespace PhotoGallery.Domain.Duplicates;

/// <summary>Which side of a duplicate set an asset sits on.</summary>
public enum DuplicateRole
{
    /// <summary>The copy that stays where it is.</summary>
    Keeper = 0,

    /// <summary>The copy proposed for quarantine. Never deleted outright.</summary>
    Redundant = 1,

    /// <summary>
    /// Neither, because nothing separates the copies and the app suggests none
    /// of them (PRP 14). Nothing acts on an undecided copy until somebody picks
    /// one to keep.
    /// </summary>
    Undecided = 2,
}
