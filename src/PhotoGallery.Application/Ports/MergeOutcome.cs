using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.Ports;

/// <summary>What a merge actually changed, by count and by kind.</summary>
/// <remarks>
/// Reported rather than summed up as a success. A merge that says nothing is a
/// merge nobody can trust or undo, and every number here is one somebody might
/// want to go and look at: a name that arrived, a name that was replaced, an
/// album a photograph left, an answer still waiting for its picture.
/// </remarks>
/// <param name="Held">
/// Answers about photographs this library has not indexed yet. Not a failure -
/// they are kept and applied by the next scan - but the one number the screen
/// must never hide, because it is the difference between "nothing to do" and
/// "three thousand answers are waiting for a scan you have not run".
/// </param>
public sealed record MergeOutcome(
    int PeopleGained,
    int PeopleRenamed,
    int PeopleDeleted,
    int NamesGained,
    int NamesReplaced,
    int FacesSetAside,
    int PhotographsTurned,
    int AlbumsChanged,
    int PhotographsMoved,
    int Held,
    IReadOnlyList<AlbumMove> Moves,
    IReadOnlyList<PersonJoin> Joins,
    IReadOnlyList<RefusedSet> Refused,
    bool WasCancelled)
{
    public static MergeOutcome Nothing { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], [], [], false);

    /// <summary>Whether anything at all came of it.</summary>
    public bool ChangedNothing =>
        PeopleGained == 0
        && PeopleRenamed == 0
        && PeopleDeleted == 0
        && NamesGained == 0
        && NamesReplaced == 0
        && FacesSetAside == 0
        && PhotographsTurned == 0
        && AlbumsChanged == 0
        && PhotographsMoved == 0
        && Held == 0;
}
