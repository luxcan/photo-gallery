using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// What the Sync screen opens showing, before anybody presses anything.
/// </summary>
/// <param name="Folder">
/// The folder this library shares answers through, or empty where none has been
/// chosen. The screen opens saying what sharing is for before this is
/// nominated, so empty is the ordinary first state rather than a fault.
/// </param>
/// <param name="Problem">
/// Why nothing can be exchanged at the moment, in the user's words, or empty
/// when it can. A folder on a drive that is not plugged in is the common case,
/// and it is worth saying rather than discovering on the button.
/// </param>
/// <param name="Machines">
/// The other computers in the house, most recently shared first.
/// </param>
/// <param name="Held">
/// Answers waiting for their photographs, split three ways. Shown because it is
/// the difference between "nothing to do" and "an evening's work is waiting for
/// a folder nobody has added" - and split because only one of the three is
/// anything the reader can act on. See <see cref="HeldStanding"/>.
/// </param>
public sealed record SharingStatus(
    string Folder,
    string Problem,
    IReadOnlyList<MachineStanding> Machines,
    HeldStanding Held)
{
    public static SharingStatus Nothing { get; } =
        new(string.Empty, string.Empty, [], HeldStanding.Nothing);

    /// <summary>Whether a folder is chosen and reachable.</summary>
    public bool CanShare => Folder.Length > 0 && Problem.Length == 0;
}
