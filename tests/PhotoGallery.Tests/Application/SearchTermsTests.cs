using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Search;

namespace PhotoGallery.Tests.Application;

/// <summary>
/// Reading one typed line as a person, a place and a description.
/// </summary>
/// <remarks>
/// Pure, and tested on its own, because getting it wrong is not visible: a bad
/// split does not fail, it quietly searches for the wrong thing and returns
/// photographs that look like a plausible answer to a question nobody asked.
/// </remarks>
public sealed class SearchTermsTests
{
    private static readonly PersonDirectoryEntry[] s_people =
    [
        new(1, "Ana Lim", 400),
        new(2, "Ana", 12),
        new(3, "Ali", 30),
    ];

    private static readonly PlaceDirectoryEntry[] s_places =
    [
        new(PlaceFilter.Exactly(10), "Sentosa", 40),
        new(PlaceFilter.Exactly(11), "Tampines Estate", 13),
        new(PlaceFilter.Exactly(12), "Singapore", 4),

        // A real place whose name is also an ordinary word. It is here to hold
        // the line that only places with photographs in them are matched at all -
        // the whole gazetteer would make half the vocabulary unusable.
        new(PlaceFilter.Exactly(13), "Bukit Timah", 2),

        // A country, which the box treats exactly as it treats a place and
        // which is the only way "Hong Kong" reaches its districts.
        new(PlaceFilter.InCountry("HK"), "Hong Kong", 120),
    ];

    [Fact]
    public void Split_TakesTheNameOutAndLeavesTheDescription()
    {
        SearchTerms terms = SearchTerms.Split("ana Lim beach", s_people, s_places);

        Assert.Equal(1, terms.PersonId);
        Assert.Equal("Ana Lim", terms.PersonName);
        Assert.Equal("beach", terms.Content);
    }

    [Fact]
    public void Split_PrefersTheLongerNameSoTheRemainderStillMeansSomething()
    {
        // "Ana" also matches here. Taking it would leave "le beach", which
        // describes nothing at all.
        SearchTerms terms = SearchTerms.Split("Ana Lim at the beach", s_people, s_places);

        Assert.Equal("Ana Lim", terms.PersonName);
        Assert.Equal("at the beach", terms.Content);
    }

    [Fact]
    public void Split_TreatsAQueryWithNoNameAsAllDescription()
    {
        SearchTerms terms = SearchTerms.Split("books, cakes", s_people, s_places);

        Assert.Null(terms.PersonId);
        Assert.Equal("books, cakes", terms.Content);
    }

    [Fact]
    public void Split_KeepsANameOnItsOwnWorkingAsItDoesToday()
    {
        SearchTerms terms = SearchTerms.Split("  Ana Lim  ", s_people, s_places);

        Assert.Equal(1, terms.PersonId);
        Assert.False(terms.HasContent);
        Assert.False(terms.IsEmpty);
    }

    [Fact]
    public void Split_DoesNotFindANameInsideALongerWord()
    {
        // Somebody called Ali must not be found in "alighting", or half the
        // vocabulary becomes unusable in a library with short names in it.
        SearchTerms terms = SearchTerms.Split("alighting at the station", s_people, s_places);

        Assert.Null(terms.PersonId);
        Assert.Equal("alighting at the station", terms.Content);
    }

    [Fact]
    public void Split_FindsANameAtTheEndAndClosesTheGap()
    {
        SearchTerms terms = SearchTerms.Split("birthday cake Ana Lim", s_people, s_places);

        Assert.Equal("Ana Lim", terms.PersonName);
        Assert.Equal("birthday cake", terms.Content);
    }

    [Fact]
    public void Split_FindsANameInTheMiddleWithoutLeavingTwoSpaces()
    {
        SearchTerms terms = SearchTerms.Split("cake Ali beach", s_people, s_places);

        Assert.Equal("Ali", terms.PersonName);
        Assert.Equal("cake beach", terms.Content);
    }

    [Fact]
    public void Split_ReadsAnEmptyBoxAsAskingForNothing()
    {
        Assert.True(SearchTerms.Split("   ", s_people, s_places).IsEmpty);
        Assert.True(SearchTerms.Split(null, s_people, s_places).IsEmpty);
    }

    [Fact]
    public void Split_AnswersWithTheNameAsTheLibrarySpellsIt()
    {
        // What is echoed back has to be the library's spelling, or the screen
        // teaches the user a name that does not exist.
        SearchTerms terms = SearchTerms.Split("ANA LIM snow", s_people, s_places);

        Assert.Equal("Ana Lim", terms.PersonName);
        Assert.Equal("Ana Lim · snow", terms.Describe());
    }

    [Fact]
    public void Split_LeavesASecondNameAsAWordRatherThanNarrowingSilently()
    {
        // Two people means photographs with both in them, which the index cannot
        // answer as one filter. Answering a narrower question than was asked
        // would be worse than treating the second name as a description.
        SearchTerms terms = SearchTerms.Split("Ana Lim Ali", s_people, s_places);

        Assert.Equal("Ana Lim", terms.PersonName);
        Assert.Equal("Ali", terms.Content);
    }

    [Fact]
    public void Split_TakesAPlaceOutAndLeavesTheDescription()
    {
        SearchTerms terms = SearchTerms.Split("sentosa sunset", s_people, s_places);

        Assert.Equal(PlaceFilter.Exactly(10), terms.Place);
        Assert.Equal("Sentosa", terms.PlaceName);
        Assert.Equal("sunset", terms.Content);
        Assert.Null(terms.PersonId);
    }

    /// <summary>
    /// A person and a place in one line fill both slots.
    /// </summary>
    /// <remarks>
    /// The reason the two directories are walked separately rather than as one
    /// list. Taking only the longest match of the two would leave "Sentosa"
    /// handed to the encoder as a description, which is a question nobody asked.
    /// </remarks>
    [Fact]
    public void Split_ReadsAPersonAndAPlaceInTheSameLine()
    {
        SearchTerms terms = SearchTerms.Split("Ana Lim sentosa beach", s_people, s_places);

        Assert.Equal(1, terms.PersonId);
        Assert.Equal(PlaceFilter.Exactly(10), terms.Place);
        Assert.Equal("beach", terms.Content);
        Assert.Equal("Ana Lim · Sentosa · beach", terms.Describe());
    }

    [Fact]
    public void Split_AnswersAPlaceOnItsOwnWithNothingToRankBy()
    {
        SearchTerms terms = SearchTerms.Split("Tampines Estate", s_people, s_places);

        Assert.Equal(PlaceFilter.Exactly(11), terms.Place);
        Assert.False(terms.HasContent);
        Assert.False(terms.IsEmpty);
        Assert.Equal("Tampines Estate", terms.Describe());
    }

    /// <summary>
    /// The longest place wins, as the longest name does.
    /// </summary>
    /// <remarks>
    /// The gazetteer names neighbourhoods, so a library holds both "Singapore"
    /// and places whose names contain it. Taking the shorter one first would
    /// leave a remainder that describes nothing.
    /// </remarks>
    [Fact]
    public void Split_PrefersTheLongerPlaceName()
    {
        SearchTerms terms = SearchTerms.Split("tampines estate", s_people, s_places);

        Assert.Equal(PlaceFilter.Exactly(11), terms.Place);
        Assert.False(terms.HasContent);
    }

    /// <summary>
    /// A word that is only part of a place name is not a place.
    /// </summary>
    /// <remarks>
    /// Deliberate, and the limit worth knowing about: the gazetteer files this
    /// library's Tampines photographs under "Tampines Estate", so typing
    /// "tampines" alone is a description, not a filter. The dropdown is what
    /// bridges that - it matches on any part of the name - and this holds the
    /// typed line to whole names so a partial word cannot silently narrow the
    /// grid to one of several places that share it.
    /// </remarks>
    [Fact]
    public void Split_DoesNotTakeHalfAPlaceName()
    {
        SearchTerms terms = SearchTerms.Split("tampines", s_people, s_places);

        Assert.Null(terms.Place);
        Assert.Equal("tampines", terms.Content);
    }

    [Fact]
    public void Split_MatchesAPlaceOnWholeWordsOnly()
    {
        // "Sentosa" must not be found inside a longer word, exactly as a person's
        // name must not be found inside "alighting".
        SearchTerms terms = SearchTerms.Split("sentosanight", s_people, s_places);

        Assert.Null(terms.Place);
        Assert.Equal("sentosanight", terms.Content);
    }

    /// <summary>
    /// On an exact tie, the person wins.
    /// </summary>
    /// <remarks>
    /// The people directory was written by the user about their own family; the
    /// places came out of a gazetteer nobody here chose. When one word could be
    /// either, the one somebody deliberately wrote down is the better guess.
    /// </remarks>
    [Fact]
    public void Split_PrefersAPersonToAPlaceOfTheSameName()
    {
        PersonDirectoryEntry[] people = [new(7, "Sentosa", 3)];

        SearchTerms terms = SearchTerms.Split("sentosa", people, s_places);

        Assert.Equal(7, terms.PersonId);
        Assert.Null(terms.Place);
    }

    /// <summary>
    /// A country is matched exactly as a place is, which is the whole point.
    /// </summary>
    /// <remarks>
    /// "hongkong" returned Taipei 101 before this existed: no place is called
    /// "Hong Kong" in a gazetteer of populated places, so the word fell through
    /// to the description search and matched every dense Asian skyline in the
    /// library.
    /// </remarks>
    [Fact]
    public void Split_ReadsACountryAsAPlace()
    {
        SearchTerms terms = SearchTerms.Split("hong kong harbour", s_people, s_places);

        Assert.Equal(PlaceFilter.InCountry("HK"), terms.Place);
        Assert.Equal(PlaceScope.Country, terms.Place!.Value.Scope);
        Assert.Equal("Hong Kong", terms.PlaceName);
        Assert.Equal("harbour", terms.Content);
    }

    [Fact]
    public void Split_ReadsAPersonAndACountryInTheSameLine()
    {
        SearchTerms terms = SearchTerms.Split("Ali hong kong", s_people, s_places);

        Assert.Equal(3, terms.PersonId);
        Assert.Equal(PlaceFilter.InCountry("HK"), terms.Place);
        Assert.Equal("Ali · Hong Kong", terms.Describe());
    }
}
