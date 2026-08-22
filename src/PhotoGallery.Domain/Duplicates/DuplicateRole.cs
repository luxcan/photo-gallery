namespace PhotoGallery.Domain.Duplicates;

/// <summary>Which side of a duplicate set an asset sits on.</summary>
public enum DuplicateRole
{
    /// <summary>The copy that stays where it is.</summary>
    Keeper = 0,

    /// <summary>The copy proposed for quarantine. Never deleted outright.</summary>
    Redundant = 1,
}
