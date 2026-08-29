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
    /// Everything this library has decided about some photographs, in the shape
    /// a held answer is stored in.
    /// </summary>
    /// <remarks>
    /// Asked when those photographs are about to leave the index, so that what
    /// somebody decided about them survives the row going away. Quarantine is
    /// why: setting a duplicate aside moves the file off the shared drive, every
    /// other machine's next scan finds it gone and removes the row, and a restore
    /// later brings the photograph back to three laptops that have never named
    /// it. Parking the decisions turns that back into an answer waiting for its
    /// photograph, which is a thing this feature already knows how to finish.
    ///
    /// <para>It covers the ordinary accidents too, which look identical to a
    /// deletion at scan time: a folder moved and moved back, a drive remounted,
    /// a tidy-up somebody undoes.</para>
    /// </remarks>
    Task<HeldAnswers> AboutAsync(
        IReadOnlyList<int> assetIds,
        MachineIdentity machine,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything this library's decode has already worked out, offered to the
    /// others.
    /// </summary>
    /// <remarks>
    /// The whole result of the preparing pass rather than a file list. A machine
    /// that took the pictures and not these facts would get a library with no
    /// timeline, no places and no albums.
    /// </remarks>
    Task<PreparedSet> PreparedAsync(
        MachineIdentity machine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Photographs this library has indexed and not prepared, which is what the
    /// pool can fill in.
    /// </summary>
    Task<IReadOnlyList<Unprepared>> UnpreparedAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every cached picture this library holds, with whether anybody has turned
    /// it.
    /// </summary>
    /// <remarks>
    /// The rotation comes with the name because a turn rewrites both files in
    /// place under a name derived from the original's bytes, which the turn does
    /// not change - so a straightened picture is unfit to pool and there is
    /// nothing in its name to say so.
    /// </remarks>
    Task<IReadOnlyList<PooledRendition>> RenditionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>How many answers are waiting, without reading any of them.</summary>
    /// <remarks>
    /// Separate from <see cref="WaitingAsync"/> because the Sharing screen asks
    /// this every time it opens and only wants the number. Nine thousand held
    /// answers is nine thousand payloads to parse for a line of text nobody
    /// reads twice.
    /// </remarks>
    Task<int> WaitingCountAsync(CancellationToken cancellationToken = default);

    /// <summary>The other machines this library has taken answers from.</summary>
    /// <remarks>
    /// Here rather than behind a port of its own because it is read for one
    /// screen and written by one merge, and a port with a single query on it is
    /// a file to open before you can read the query.
    /// </remarks>
    Task<IReadOnlyList<Peer>> PeersAsync(CancellationToken cancellationToken = default);

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
