namespace PhotoGallery.App.Sharing;

/// <summary>
/// Asks what to call another computer, answered by the window.
/// </summary>
public interface IMachineNameQuestion
{
    /// <summary>Asks for a name for a computer.</summary>
    /// <param name="current">What the screen calls it now.</param>
    /// <param name="ownName">What it calls itself, which an empty box goes back to.</param>
    /// <param name="problem">Why the last name typed was not kept, or null.</param>
    /// <returns>What was typed, or null where the question was cancelled.</returns>
    string? AskName(string current, string ownName, string? problem);
}
