using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.Ports;

/// <summary>The read side of what this library has decided.</summary>
/// <remarks>
/// Two questions that look alike and are not. A decision set is what this
/// library has <em>said</em>; the contents are what it <em>has</em>. A library
/// holds a great many photographs nobody has decided anything about, and
/// answering the second question with the first would hold answers about
/// pictures sitting right there.
/// </remarks>
public interface IDecisionReader
{
    /// <summary>
    /// Everything this library holds, proposals included.
    /// </summary>
    /// <remarks>
    /// With the proposals, because the merge needs them: a confirmation arriving
    /// from another machine has to be able to beat one. Publishing drops them -
    /// see <see cref="DecisionSet.WithoutProposals"/>.
    /// </remarks>
    Task<DecisionSet> ReadAsync(
        MachineIdentity machine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which photographs this library has indexed and which faces it has found,
    /// which is what decides whether an answer lands or waits.
    /// </summary>
    Task<LibraryContents> ContentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The answers parked against photographs this library had not indexed when
    /// they arrived.
    /// </summary>
    /// <remarks>
    /// Read whole, because settling them needs them whole: two of them can be
    /// about the same face, and which one stands is decided by comparing them
    /// against each other. There is no cheaper question to ask - a sweep that
    /// only read the answers about photographs the last scan added would miss
    /// the ones waiting on the face pass, which is most of them.
    /// </remarks>
    Task<HeldAnswers> WaitingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The rows a set of merged turns would land on, with the rendition each one
    /// would have to move.
    /// </summary>
    /// <remarks>
    /// A turn is the one decision that is not only a row. The cached pictures
    /// turn and the boxes drawn on them turn with them, so applying one needs
    /// more than the key.
    /// </remarks>
    Task<IReadOnlyList<TurnTarget>> TurnTargetsAsync(
        IReadOnlyList<AssetKey> photographs, CancellationToken cancellationToken = default);
}
