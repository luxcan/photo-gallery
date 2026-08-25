using PhotoGallery.Domain.Collections;

namespace PhotoGallery.Application.Ports;

/// <summary>Reads and writes the collections, and what the user has said about them.</summary>
public interface ICollectionRepository
{
    /// <summary>
    /// Every photograph the clusterer can place on a timeline.
    /// </summary>
    /// <remarks>
    /// Prepared, in the library, and carrying a capture date. Photographs
    /// without one are left out rather than guessed at, and photographs already
    /// in a collection somebody made or kept are left out too - the pass may
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
    /// longer offered goes; a collection the user kept or made never does.
    /// </remarks>
    Task<int> SaveProposalsAsync(
        IReadOnlyList<ProposedCollection> proposals,
        CancellationToken cancellationToken = default);

    /// <summary>The collections themselves, for the screen.</summary>
    Task<IReadOnlyList<CollectionSummary>> GetAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Which collection a photograph is in, if any.</summary>
    /// <remarks>
    /// At most one, always - the membership table's key says so - which is why
    /// this answers with a collection rather than a list.
    /// </remarks>
    Task<CollectionSummary?> FindForAssetAsync(
        int assetId, CancellationToken cancellationToken = default);

    /// <summary>One collection's photographs, in the order they were taken.</summary>
    Task<IReadOnlyList<int>> GetMembersAsync(
        int collectionId, CancellationToken cancellationToken = default);

    /// <summary>Makes a collection of the user's own, and returns its id.</summary>
    Task<int> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>What one collection is looking for.</summary>
    Task<CollectionRule> GetRuleAsync(
        int collectionId, CancellationToken cancellationToken = default);

    /// <summary>Sets what it is looking for, replacing whatever was there.</summary>
    Task SetRuleAsync(
        int collectionId, CollectionRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// The photographs that fit a collection's rule and are not in it yet.
    /// </summary>
    /// <remarks>
    /// Suggestions, not additions: nothing is put anywhere until the user says
    /// so. Photographs already in another collection are left out - one
    /// collection each - and so is anything refused for this one before.
    /// </remarks>
    Task<IReadOnlyList<int>> SuggestAsync(
        int collectionId, CancellationToken cancellationToken = default);

    /// <summary>Keeps a proposal, so no pass may change it again.</summary>
    Task AcceptAsync(int collectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws a proposal away and remembers every photograph that was in it.
    /// </summary>
    Task DismissAsync(int collectionId, CancellationToken cancellationToken = default);

    /// <summary>Renames one, and records that the name is the user's now.</summary>
    Task RenameAsync(int collectionId, string name, CancellationToken cancellationToken = default);

    /// <summary>Removes a collection the user made, leaving its photographs loose.</summary>
    Task DeleteAsync(int collectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts photographs into a collection, taking them out of wherever they were.
    /// </summary>
    /// <remarks>
    /// A photograph belongs to at most one collection, so this is a move rather
    /// than an addition, and the result says what it moved them out of - a rule
    /// the user did not ask about must not be enforced silently.
    /// </remarks>
    Task<CollectionMoveResult> AddAsync(
        int collectionId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes photographs out, and remembers the refusal when it was the app's idea.
    /// </summary>
    /// <remarks>
    /// Removing from a proposal is a rejection: that photograph is never offered
    /// for that span again. Removing from a collection the user made is nothing
    /// of the sort - they are simply rearranging their own shelf.
    /// </remarks>
    Task RemoveAsync(
        int collectionId,
        IReadOnlyList<int> assetIds,
        CancellationToken cancellationToken = default);
}
