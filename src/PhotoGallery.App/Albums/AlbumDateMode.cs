namespace PhotoGallery.App.Albums;

/// <summary>
/// What an album's rule asks about when a photograph was taken.
/// </summary>
/// <remarks>
/// Asked outright rather than inferred from which boxes were filled in. Two
/// date boxes left the question hanging: one box filled meant "that day
/// onwards" to the rule and "that day" to most people, and nothing on the panel
/// said which. Three named answers cost one row and remove the guess.
/// </remarks>
public enum AlbumDateMode
{
    /// <summary>The rule asks nothing about the date.</summary>
    Any,

    /// <summary>One day exactly.</summary>
    OneDay,

    /// <summary>Everything between two days, both included.</summary>
    Range,
}
