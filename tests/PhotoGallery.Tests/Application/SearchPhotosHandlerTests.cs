using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Search;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Search;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Answering one typed line with a filter and an order.
/// </summary>
/// <remarks>
/// Tested against a stub encoder rather than the real one. What is under test is
/// how the two halves of a query compose, and 1.7 GB of weights would only make
/// that slower to find out.
/// </remarks>
public sealed class SearchPhotosHandlerTests
{
    private static readonly PersonDirectoryEntry[] s_people =
    [
        new(1, "Ana Lim", 400),
        new(2, "Ali", 30),
    ];

    [Fact]
    public async Task Search_ByNameAloneAsksNoModelAndRanksNothing()
    {
        // The behaviour that existed before this feature, unchanged: a name is a
        // filter, and answering it should not load a vector or wake an encoder.
        var encoder = new CountingEncoder(Vector(1f));
        var content = new StubContent([]);

        PhotoSearch search = await Handler(encoder, content).HandleAsync("Ana Lim");

        Assert.Equal(1, search.Terms.PersonId);
        Assert.Null(search.Ranked);
        Assert.Equal(0, encoder.Asked);
        Assert.Equal(0, content.Reads);
    }

    [Fact]
    public async Task Search_ByDescriptionRanksEveryPhotographBestFirst()
    {
        var content = new StubContent(
        [
            new ContentVector(10, Vector(0.1f)),
            new ContentVector(11, Vector(0.9f)),
            new ContentVector(12, Vector(0.5f)),
        ]);

        PhotoSearch search =
            await Handler(new CountingEncoder(Vector(1f)), content).HandleAsync("beach");

        Assert.Null(search.Terms.PersonId);
        Assert.Equal([11, 12, 10], search.Ranked);
    }

    [Fact]
    public async Task Search_NarrowsToThePersonBeforeItRanks()
    {
        // The trap this design exists to avoid. Ranking the library and then
        // keeping the person's would answer "nothing" whenever her best beach
        // is not among the best beaches overall - and in twelve years of
        // photographs it usually is not.
        var content = new StubContent(
            everyone:
            [
                new ContentVector(10, Vector(0.9f)),
                new ContentVector(11, Vector(0.8f)),
                new ContentVector(12, Vector(0.1f)),
            ],
            byPerson: new Dictionary<int, ContentVector[]>
            {
                [1] = [new ContentVector(12, Vector(0.1f))],
            });

        PhotoSearch search =
            await Handler(new CountingEncoder(Vector(1f)), content).HandleAsync("Ana Lim beach");

        Assert.Equal(1, search.Terms.PersonId);
        Assert.Equal("beach", search.Terms.Content);

        // Her only photograph, however poorly it scores against the rest.
        Assert.Equal([12], search.Ranked);
        Assert.Equal(1, content.LastPersonId);
    }

    [Fact]
    public async Task Search_CapsWhatADescriptionMayReturn()
    {
        // A ranking has no natural end: every picture scores something, and the
        // thousandth is not a beach.
        ContentVector[] many =
        [
            .. Enumerable.Range(1, SearchPhotosHandler.MaxResults + 50)
                .Select(id => new ContentVector(id, Vector(1f / id))),
        ];

        PhotoSearch search = await Handler(new CountingEncoder(Vector(1f)), new StubContent(many))
            .HandleAsync("beach");

        Assert.Equal(SearchPhotosHandler.MaxResults, search.Ranked!.Count);
    }

    [Fact]
    public async Task Search_TellsAnUndescribedLibraryApartFromNoMatches()
    {
        // One of these the user can do something about, and the screen has to be
        // able to say which.
        PhotoSearch search = await Handler(new CountingEncoder(Vector(1f)), new StubContent([]))
            .HandleAsync("beach");

        Assert.True(search.NothingIndexed);
        Assert.Empty(search.Ranked!);
    }

    [Fact]
    public async Task Search_OfNothingAsksForNothing()
    {
        PhotoSearch search = await Handler(new CountingEncoder(Vector(1f)), new StubContent([]))
            .HandleAsync("   ");

        Assert.True(search.IsEmpty);
        Assert.Null(search.Ranked);
    }

    private static readonly PlaceDirectoryEntry[] s_places =
    [
        new(PlaceFilter.Exactly(10), "Sentosa", 40),
        new(PlaceFilter.InCountry("HK"), "Hong Kong", 120),
    ];

    [Fact]
    public async Task Search_ByPlaceAloneAsksNoModelAndRanksNothing()
    {
        // The point of keeping the filter separate from the ranking: somebody who
        // has never installed the description models can still search by place.
        var encoder = new CountingEncoder(Vector(1f));
        var content = new StubContent([]);

        PhotoSearch search =
            await Handler(encoder, content, s_places).HandleAsync("Sentosa");

        Assert.Equal(PlaceFilter.Exactly(10), search.Terms.Place);
        Assert.Null(search.Ranked);
        Assert.Equal(0, encoder.Asked);
        Assert.Equal(0, content.Reads);
    }

    /// <summary>
    /// A country is a place too, and the reason the whole scope exists.
    /// </summary>
    /// <remarks>
    /// The gazetteer names populated places, so a dense city resolves to its
    /// districts - Hong Kong photographs come back as Tsim Sha Tsui and Central,
    /// and "hongkong" matched nothing at all until this. Typing the word somebody
    /// would actually use has to reach every district under it.
    /// </remarks>
    [Fact]
    public async Task Search_ByCountryNamesTheWholeCountryRatherThanADistrict()
    {
        var content = new StubContent([]);

        PhotoSearch search = await Handler(new CountingEncoder(Vector(1f)), content, s_places)
            .HandleAsync("Hong Kong");

        Assert.Equal(PlaceFilter.InCountry("HK"), search.Terms.Place);
        Assert.Equal(PlaceScope.Country, search.Terms.Place!.Value.Scope);
        Assert.Equal("Hong Kong", search.Terms.PlaceName);
        Assert.False(search.Terms.HasContent);
    }

    [Fact]
    public async Task Search_NarrowsToThePlaceBeforeItRanks()
    {
        // The same trap as the person filter. The best three hundred beaches in
        // the library may include none on Sentosa, and a search that answered
        // "nothing" while holding one would be worse than useless.
        var content = new StubContent(
            everyone:
            [
                new ContentVector(10, Vector(0.9f)),
                new ContentVector(11, Vector(0.8f)),
                new ContentVector(12, Vector(0.1f)),
            ],
            byPlace: new Dictionary<int, ContentVector[]>
            {
                [10] = [new ContentVector(12, Vector(0.1f))],
            });

        PhotoSearch search = await Handler(new CountingEncoder(Vector(1f)), content, s_places)
            .HandleAsync("sentosa beach");

        Assert.Equal(PlaceFilter.Exactly(10), search.Terms.Place);
        Assert.Equal("beach", search.Terms.Content);
        Assert.Equal(PlaceFilter.Exactly(10), content.LastPlace);

        // The one photograph there, however poorly it scores against the rest.
        Assert.Equal([12], search.Ranked);
    }

    private static SearchPhotosHandler Handler(
        IContentEncoder encoder, IContentRepository content, params PlaceDirectoryEntry[] places) =>
        new(new StubPeople(), new StubPlaces(places), encoder, content);

    private sealed class StubPlaces : IPlaceReader
    {
        private readonly PlaceDirectoryEntry[] _places;

        public StubPlaces(PlaceDirectoryEntry[] places) => _places = places;

        public Task<IReadOnlyList<PlaceDirectoryEntry>> GetDirectoryAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<PlaceDirectoryEntry>>(_places);
    }

    /// <summary>A vector pointing however far along the first axis is asked for.</summary>
    private static ContentEmbedding Vector(float towards)
    {
        float[] values = new float[ContentEmbedding.Dimensions];
        values[0] = towards;
        values[1] = MathF.Sqrt(Math.Max(0f, 1f - (towards * towards)));
        return new ContentEmbedding(values);
    }

    private sealed class StubPeople : IPeopleReader
    {
        public Task<IReadOnlyList<FaceRecord>> GetFacesAsync(
            bool includeEmbeddings, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRecord>>([]);

        public Task<IReadOnlyList<FaceSample>> GetSamplesAsync(
            int personId, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceSample>>([]);

        public Task<IReadOnlyList<FaceOnPhoto>> GetFacesOnAsync(
            int assetId, CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceOnPhoto>>([]);

        public Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<Person>>([]);

        public Task<IReadOnlyList<PersonDirectoryEntry>> GetDirectoryAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<PersonDirectoryEntry>>(s_people);

        public Task<IReadOnlyList<FaceRejection>> GetRejectionsAsync(
            CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<FaceRejection>>([]);
    }

    private sealed class CountingEncoder : IContentEncoder
    {
        private readonly ContentEmbedding _answer;

        public CountingEncoder(ContentEmbedding answer) => _answer = answer;

        public int Asked { get; private set; }

        public Task<ContentEmbedding?> DescribePictureAsync(
            string previewPath, CancellationToken token = default) =>
            Task.FromResult<ContentEmbedding?>(_answer);

        public Task<ContentEmbedding?> DescribePhraseAsync(
            string phrase, CancellationToken token = default)
        {
            Asked++;
            return Task.FromResult<ContentEmbedding?>(_answer);
        }
    }

    private sealed class StubContent : IContentRepository
    {
        private readonly ContentVector[] _everyone;
        private readonly Dictionary<int, ContentVector[]> _byPerson;
        private readonly Dictionary<int, ContentVector[]> _byPlace;

        public StubContent(
            ContentVector[] everyone,
            Dictionary<int, ContentVector[]>? byPerson = null,
            Dictionary<int, ContentVector[]>? byPlace = null)
        {
            _everyone = everyone;
            _byPerson = byPerson ?? [];
            _byPlace = byPlace ?? [];
        }

        public int Reads { get; private set; }

        public int? LastPersonId { get; private set; }

        public PlaceFilter? LastPlace { get; private set; }

        public Task SaveAsync(
            IReadOnlyList<ContentScanUpdate> updates, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ContentVector>> GetVectorsAsync(
            int? personId = null, PlaceFilter? place = null, CancellationToken token = default)
        {
            Reads++;
            LastPersonId = personId;
            LastPlace = place;

            return Task.FromResult<IReadOnlyList<ContentVector>>(
                (personId, place) switch
                {
                    (int who, _) => _byPerson.TryGetValue(who, out ContentVector[]? theirs)
                        ? theirs
                        : [],
                    (null, PlaceFilter there) =>
                        _byPlace.TryGetValue(there.PlaceId, out ContentVector[]? here) ? here : [],
                    _ => _everyone,
                });
        }

        public Task<(int Described, int Total)> CountAsync(CancellationToken token = default) =>
            Task.FromResult((_everyone.Length, _everyone.Length));
    }
}
