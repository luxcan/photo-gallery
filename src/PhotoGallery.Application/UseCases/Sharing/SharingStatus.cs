using PhotoGallery.Domain.Sharing.Direct;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// What the Sharing screen opens showing, before anybody presses anything.
/// </summary>
/// <param name="Folder">
/// The folder this library shares answers through, or empty where none has been
/// chosen. The screen opens saying what will and will not be sent before this
/// is nominated, so empty is the ordinary first state rather than a fault.
/// </param>
/// <param name="Problem">
/// Why nothing can be exchanged at the moment, in the user's words, or empty
/// when it can. A folder on a drive that is not plugged in is the common case,
/// and it is worth saying rather than discovering on the button.
/// </param>
/// <param name="Machines">
/// The other computers in the house, most recently shared first.
/// </param>
/// <param name="Waiting">
/// Answers held for photographs this library has not indexed. Shown because it
/// is the difference between "nothing to do" and "an evening's work is waiting
/// for a folder nobody has added".
/// </param>
/// <param name="Unprepared">
/// Photographs this library has indexed and not yet prepared - what the pictures
/// half is worth, in the only unit that means anything to somebody deciding
/// whether to spend five minutes.
/// </param>
public sealed record SharingStatus(
    string Folder,
    string Problem,
    IReadOnlyList<MachineStanding> Machines,
    int Waiting,
    int Unprepared,
    DiscoveryProblem Discovery = DiscoveryProblem.None)
{
    public static SharingStatus Nothing { get; } = new(string.Empty, string.Empty, [], 0, 0);

    /// <summary>
    /// Why the other computers cannot be found on the network, said plainly, or
    /// empty when they can.
    /// </summary>
    /// <remarks>
    /// Said rather than discovered. Discovery blocked by a Public network
    /// profile finds nothing and raises no error at all, so an empty list is the
    /// same picture as nobody being there - and the two need completely
    /// different things from the person reading it.
    /// </remarks>
    public string DiscoveryProblem => Discovery.Explain();

    /// <summary>Whether a folder is chosen and reachable.</summary>
    public bool CanShare => Folder.Length > 0 && Problem.Length == 0;
}
