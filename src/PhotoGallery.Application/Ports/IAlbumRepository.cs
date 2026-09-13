using PhotoGallery.Domain.Albums;

namespace PhotoGallery.Application.Ports;

/// <summary>Reads and writes the albums, and what the user has said about them.</summary>
public interface IAlbumRepository
{
    /// <summary>
    /// Every photograph the clusterer can place on a timeline.
    /// </summary>
    /// <remarks>
    /// Prepared, in the library, and carrying a capture date. Photographs
    /// without one are left out rather than guessed at, and photographs already
    /// in an album somebody made or kept are left out too - the pass may
    /// only group what nobody has spoken for.
    /// </remarks>
    Task<IReadOnlyList<DatedPhoto>> GetCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Every rejection, as spans mapped to the photographs refused in them.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<int>>> GetRejectionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes what the clusterer proposed, leaving alone anything the user owns.
    /// </summary>
    /// <remarks>
    /// Matched to what is already there by span, never by id: a proposal is a
    /// derived row and its id changes every rebuild. A proposal whose span is no
    /// longer offered goes; an album the user kept or made never does.
    /// </remarks>
    Task<int> SaveProposalsAsync(
        IReadOnlyList<ProposedAlbum> proposals,
        CancellationToken cancellationToken = default);

    /// <summary>The albums themselves, for the screen.</summary>
    Task<IReadOnlyList<AlbumSummary>> GetAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Which album a photograph is in, if any.</summary>
    /// <remarks>
    /// At most one, always - the membership table's key says so - which is why
    /// this answers with an album rather than a list.
    /// </remarks>
    Task<AlbumSummary?> FindForAssetAsync(
        int assetId, CancellationToken cancellationToken = default);

    /// <summary>One album's photographs, in the order they were taken.</summary>
    Task<IReadOnlyList<int>> GetMembersAsync(
        int albumId, CancellationToken cancellationToken = default);

    /// <summary>Makes an album of the user's own, and returns its id.</summary>
    Task<int> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>What one album is looking for.</summary>
    Task<AlbumRule> GetRuleAsync(
        int albumId, CancellationToken cancellationToken = default);

    /// <summary>Sets what it is looking for, replacing whatever was there.</summary>
    Task SetRuleAsync(
        int albumId, AlbumRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// The photographs that fit an album's rule and are not in it yet.
    /// </summary>
    /// <remarks>
    /// Suggestions, not additions: nothing is put anywhere until the user says
    /// so. Anything refused for this album before is left out, and so is
    /// anything an album somebody made or kept has already spoken for - one
    /// album each.
    ///
    /// <para>A photograph the app itself swept into a suggestion is offered
    /// anyway. A suggestion is a question nobody has answered, and a question
    /// must not outrank an album a person made on purpose; keeping the
    /// photograph is what takes it out of the suggestion, which
    /// <see cref="AddAsync"/> does on the way in. The clusterer's own feed
    /// draws the same line - see <see cref="GetCandidatesAsync"/>.</para>
    /// </remarks>
    Task<IReadOnlyList<int>> SuggestAsync(
        int albumId, CancellationToken cancellationToken = default);

    /// <summary>Keeps a proposal, so no pass may change it again.</summary>
    Task AcceptAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws a proposal away and remembers every photograph that was in it.
    /// </summary>
    Task DismissAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>Renames one, and records that the name is the user's now.</summary>
    Task RenameAsync(int albumId, string name, CancellationToken cancellationToken = default);

    /// <summary>Removes an album the user made, leaving its photographs loose.</summary>
    Task DeleteAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts photographs into an album, taking them out of wherever they were.
    /// </summary>
    /// <remarks>
    /// A photograph belongs to at most one album, so this is a move rather
    /// than an addition, and the result says what it moved them out of - a rule
    /// the user did not ask about must not be enforced silently.
    /// </remarks>
    Task<AlbumAddResult> AddAsync(
        int albumId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes photographs out, and remembers the refusal when it was the app's idea.
    /// </summary>
    /// <remarks>
    /// Removing from a proposal is a rejection: that photograph is never offered
    /// for that span again. Removing from an album the user made is nothing
    /// of the sort - they are simply rearranging their own shelf.
    /// </remarks>
    Task RemoveAsync(
        int albumId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes one of an album's photographs the picture shown for it.
    /// </summary>
    /// <remarks>
    /// A cover is worked out from the album's own photographs - the one with the
    /// most faces, or the middle of its span - and worked out again every time a
    /// photograph joins or leaves. This records that a person has answered the
    /// question instead, after which the rule leaves the album alone for as long
    /// as it still holds that photograph.
    ///
    /// <para>Covers do not travel between machines. Both libraries derive the
    /// same one from the same rule today, so there has never been anything to
    /// disagree about; a chosen one can disagree, and putting it in the payload
    /// is a decision of its own rather than a consequence of this.</para>
    /// </remarks>
    /// <returns>
    /// False when the album has no such photograph in it, which is the only way
    /// this refuses.
    /// </returns>
    Task<bool> SetCoverAsync(
        int albumId, int assetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that these photographs do not belong in this album, without
    /// putting them into it first.
    /// </summary>
    /// <remarks>
    /// Answering "not this one" about a suggested photograph must not disturb
    /// where that photograph already is. Both answer paths used to write a
    /// refusal by adding the photograph and taking it straight back out, which
    /// was harmless only while a photograph in a suggestion could never be
    /// offered at all. Now that it can, the round trip would quietly move it
    /// out of the suggestion it came from and leave it in no album whatsoever -
    /// as the answer to a question about a different album entirely. The
    /// refusal is the whole of the decision, so this writes only that.
    /// </remarks>
    Task RefuseAsync(
        int albumId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default);
}
