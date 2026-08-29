namespace PhotoGallery.App.Sharing;

/// <summary>
/// One other computer, as the list shows it.
/// </summary>
/// <remarks>
/// The recency is worked out once, when the screen is read, rather than bound to
/// a clock. A line that silently rewrote itself from "up to date" to "1 hour
/// ago" while somebody looked at it would be a screen that appears to be doing
/// something.
/// </remarks>
/// <param name="Waiting">
/// True for a computer that has shared answers this library has not taken. What
/// the button is for, and worth marking rather than leaving the user to compare
/// dates.
/// </param>
public sealed record MachineRow(string Name, string Recency, bool Waiting);
