using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Search;

namespace PhotoGallery.Application.UseCases.Search;

/// <summary>
/// Answers one typed line with a person to filter by and an order to show the
/// rest in.
/// </summary>
/// <remarks>
/// The two halves of a query are answered by different machinery and neither
/// competes with the other. A name is a row the index can filter on exactly; a
/// description is a direction to sort in. So "Ana Lim beach" is not two result
/// lists fighting over one ranking - it is their photographs, beaches first.
///
/// <para>Which is also why the filter is applied before the ranking is cut down.
/// The best three hundred beaches in twelve years may contain none of her, and a
/// search that answered "nothing" while holding several would be worse than
/// useless.</para>
/// </remarks>
public sealed class SearchPhotosHandler
{
    /// <summary>
    /// How many photographs a description is allowed to return.
    /// </summary>
    /// <remarks>
    /// A ranking has no natural end - every picture in the library scores
    /// something against "beach", and the thousandth is not a beach. Cutting the
    /// tail off is what makes the answer mean "these ones" rather than "all of
    /// them, in an order".
    /// </remarks>
    public const int MaxResults = 300;

    private readonly IPeopleReader _people;
    private readonly IPlaceReader _places;
    private readonly IContentEncoder _encoder;
    private readonly IContentRepository _content;

    public SearchPhotosHandler(
        IPeopleReader people,
        IPlaceReader places,
        IContentEncoder encoder,
        IContentRepository content)
    {
        _people = people;
        _places = places;
        _encoder = encoder;
        _content = content;
    }

    public async Task<PhotoSearch> HandleAsync(
        string? typed, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PersonDirectoryEntry> everyone =
            await _people.GetDirectoryAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PlaceDirectoryEntry> everywhere =
            await _places.GetDirectoryAsync(cancellationToken).ConfigureAwait(false);

        SearchTerms terms = SearchTerms.Split(typed, everyone, everywhere);

        // A name or a place on its own is answered by the filter alone, exactly
        // as it was before this feature existed. Nothing is loaded, no model is
        // asked - which is what lets somebody search by place on a machine that
        // has never installed the description models.
        if (!terms.HasContent)
        {
            return new PhotoSearch(terms, null, false);
        }

        ContentEmbedding? asked = await _encoder
            .DescribePhraseAsync(terms.Content, cancellationToken)
            .ConfigureAwait(false);

        if (asked is not ContentEmbedding query)
        {
            return new PhotoSearch(terms, null, false);
        }

        IReadOnlyList<ContentVector> vectors = await _content
            .GetVectorsAsync(terms.PersonId, terms.Place, cancellationToken)
            .ConfigureAwait(false);

        if (vectors.Count == 0)
        {
            // Nothing described yet, which is a different answer from "no
            // matches" and the screen says so.
            return new PhotoSearch(terms, [], true);
        }

        int[] ranked =
        [
            .. vectors
                .Select(vector => (vector.AssetId, Score: query.SimilarityTo(vector.Vector)))
                .OrderByDescending(match => match.Score)
                .ThenBy(match => match.AssetId)
                .Take(MaxResults)
                .Select(match => match.AssetId),
        ];

        return new PhotoSearch(terms, ranked, false);
    }
}

/// <summary>What one typed line turned out to be asking for.</summary>
/// <param name="Ranked">
/// The photographs a description matched, best first, or null when nothing was
/// described - which is not the same as an empty list.
/// </param>
/// <param name="NothingIndexed">
/// Whether the answer is empty only because the library has not been described
/// yet. Worth telling apart from "no matches", because one of them has something
/// the user can do about it.
/// </param>
public sealed record PhotoSearch(
    SearchTerms Terms, IReadOnlyList<int>? Ranked, bool NothingIndexed)
{
    /// <summary>Whether this asks for anything the gallery has to act on.</summary>
    public bool IsEmpty => Terms.IsEmpty;
}
