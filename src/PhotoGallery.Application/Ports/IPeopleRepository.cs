using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.Ports;

/// <summary>The write side of naming people.</summary>
public interface IPeopleRepository
{
    /// <summary>
    /// The person with this name, creating them if this is the first time.
    /// </summary>
    /// <remarks>
    /// Matched on the name because that is what the user typed and what they
    /// mean: naming a second group "Ana Lim" is a statement that it is the same
    /// person, not a request for a second one. Names are unique in the index for
    /// the same reason.
    /// </remarks>
    Task<int> EnsurePersonAsync(string displayName, CancellationToken cancellationToken = default);

    Task RenamePersonAsync(int personId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the year somebody was born, or clears it with <c>null</c>.
    /// </summary>
    /// <remarks>
    /// A year and nothing finer, because a year is all the screen asks for. A
    /// full date would let the app compute an exact age, and would also let it
    /// invent one: there would be no telling a birthday that was typed from a
    /// first of January that was assumed.
    /// </remarks>
    Task SetBirthYearAsync(int personId, int? birthYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Says who some faces are, replacing anything said about them before.
    /// </summary>
    Task AssignAsync(
        int personId,
        IReadOnlyList<ScoredFace> faces,
        AssignmentSource source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every proposal made to one person.
    /// </summary>
    /// <remarks>
    /// Proposals are a question, not a record. Once the person's eras change the
    /// old question was asked on out-of-date information, so it is withdrawn
    /// rather than left alongside the new one.
    /// </remarks>
    Task ClearProposalsAsync(int personId, CancellationToken cancellationToken = default);

    /// <summary>Forgets what was said about some faces entirely.</summary>
    Task UnassignAsync(
        IReadOnlyList<int> faceIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets faces aside as nobody worth tracking, or brings them back.
    /// </summary>
    /// <remarks>
    /// Strangers in the background outnumber the people a library is about, and
    /// rejecting them one person at a time never ends - a face refused as Ana Lim
    /// is still offered as everyone else. Set aside, it is offered as nobody.
    /// </remarks>
    Task SetIgnoredAsync(
        IReadOnlyList<int> faceIds,
        bool ignored,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a person's eras with the ones their confirmed faces now support.
    /// </summary>
    /// <remarks>
    /// Recomputed whole rather than adjusted, because a single confirmation can
    /// move a boundary that was drawn between two others.
    /// </remarks>
    Task ReplaceErasAsync(
        int personId,
        IReadOnlyList<PersonEra> eras,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a person, their eras and everything said about their faces.</summary>
    Task RemovePersonAsync(int personId, CancellationToken cancellationToken = default);
}
