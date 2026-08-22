using PhotoGallery.Domain.People;

namespace PhotoGallery.Application.Ports;

/// <summary>The read side of who is in the library.</summary>
public interface IPeopleReader
{
    /// <summary>
    /// Every face found so far, with whatever anyone has said about it.
    /// </summary>
    /// <remarks>
    /// All of them, in one go. See <see cref="FaceRecord"/> for why holding the
    /// whole set in memory is the design rather than a shortcut.
    /// </remarks>
    /// <param name="includeEmbeddings">
    /// Whether to read each face's vector. Only matching needs them, and they are
    /// the whole weight of this call - two kilobytes a face against a few dozen
    /// bytes for everything else. A screen that draws faces and reports who they
    /// are asks for false and pays for neither the read nor the memory; the
    /// records it gets back have an empty <see cref="FaceRecord.Embedding"/>, so
    /// nothing can quietly match against vectors it did not ask for.
    /// </param>
    Task<IReadOnlyList<FaceRecord>> GetFacesAsync(
        bool includeEmbeddings, CancellationToken cancellationToken = default);

    /// <summary>
    /// The confirmed faces of one person, as the examples their eras are built
    /// from.
    /// </summary>
    /// <remarks>
    /// Filtered in the database rather than in memory. Rebuilding one person is
    /// the commonest thing this feature does - every answer to a proposal causes
    /// one, and checking everybody causes one per person - and reading every
    /// vector in the library to keep a few hundred of them made the cost of that
    /// depend on the size of the library rather than on the size of the person.
    /// </remarks>
    Task<IReadOnlyList<FaceSample>> GetSamplesAsync(
        int personId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The faces in one photograph, with whoever they are said to be.
    /// </summary>
    /// <remarks>
    /// One picture's worth, so the viewer can draw boxes without loading the
    /// library's every vector to do it.
    /// </remarks>
    Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
        int assetId, CancellationToken cancellationToken = default);

    /// <summary>Everyone who has been named, with their eras loaded.</summary>
    Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Just the names and how many pictures each would return.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetPeopleAsync"/> because a search box asks on
    /// every keystroke and must not pay for a single vector to do it.
    /// </remarks>
    Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every "no, that is not them" the user has given.
    /// </summary>
    /// <remarks>
    /// Kept apart from the faces themselves because a rejection says nothing
    /// about who the face is - only who it is not. The face stays unnamed and
    /// keeps appearing to be named; it simply is not offered to that person
    /// again.
    /// </remarks>
    Task<IReadOnlyList<FaceRejection>> GetRejectionsAsync(
        CancellationToken cancellationToken = default);
}
