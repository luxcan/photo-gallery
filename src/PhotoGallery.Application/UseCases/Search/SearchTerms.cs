using System.Globalization;
using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Search;

/// <summary>
/// What one typed line is asking for: who should be in the picture, where it was
/// taken, and what the picture should be of.
/// </summary>
/// <remarks>
/// "ana Lim sentosa beach" is three questions in one box, and they are answered by
/// different machinery - a name and a place are rows the index can filter on
/// exactly, and "beach" is a direction to sort in. Separating them here means
/// none has to know about the others, and the screen can show what it understood
/// so a wrong split is visible rather than mysterious.
/// </remarks>
/// <param name="PersonId">Whoever was named, or null when nobody was.</param>
/// <param name="PersonName">
/// The name as it is spelt in the library rather than as it was typed, so the
/// screen can echo "Ana Lim" back at somebody who typed "ana le".
/// </param>
/// <param name="Place">
/// Wherever was named, or null when nowhere was. Either one gazetteer place or a
/// whole country - "Tsim Sha Tsui" and "Hong Kong" are the same kind of answer
/// here and differ only in how wide they are.
/// </param>
/// <param name="PlaceName">The place as the gazetteer spells it.</param>
/// <param name="Content">What is left after both, and may be empty.</param>
public readonly record struct SearchTerms(
    int? PersonId, string? PersonName, PlaceFilter? Place, string? PlaceName, string Content)
{
    /// <summary>Whether there is anything to sort by.</summary>
    public bool HasContent => Content.Length > 0;

    /// <summary>Whether anything at all was asked for.</summary>
    public bool IsEmpty => PersonId is null && Place is null && !HasContent;

    /// <summary>
    /// Splits what was typed into the name in it and the rest.
    /// </summary>
    /// <remarks>
    /// The longest name wins. A library with both a "Ana" and a "Ana Lim" would
    /// otherwise answer "ana le beach" as Ana plus "le beach", and "le beach" is
    /// not a description of anything - so the greedy match is what keeps the
    /// remainder meaningful rather than what makes the name more likely.
    ///
    /// <para>Matched on whole words only. Somebody named Ali must not be found
    /// inside "alighting", and a substring match would make half the vocabulary
    /// unusable in a library with short names in it.</para>
    ///
    /// <para>One name, not several. Two people in one query means photographs
    /// containing both, which the index cannot answer as a single filter today -
    /// and quietly answering a narrower question than was asked is worse than
    /// treating the second name as a word.</para>
    /// </remarks>
    /// <param name="places">
    /// Only the places photographs were actually taken, which is what keeps this
    /// safe. Matching against the whole gazetteer would find a town called
    /// "Beach" and quietly turn a description into a filter over nothing.
    /// </param>
    public static SearchTerms Split(
        string? typed,
        IReadOnlyList<PersonDirectoryEntry> people,
        IReadOnlyList<PlaceDirectoryEntry> places)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(places);

        string text = Collapse(typed);
        if (text.Length == 0)
        {
            return new SearchTerms(null, null, null, null, string.Empty);
        }

        // People before places on an exact tie of length. The names in the people
        // directory were typed by the user about their own family; the places
        // came out of a gazetteer nobody here chose. When both could be meant,
        // the one somebody deliberately wrote down wins.
        (int? personId, string? personName, text) = TakeName(
            text,
            [
                .. people
                    .Where(person => !string.IsNullOrWhiteSpace(person.DisplayName))
                    .Select(person => (person.Id, Name: person.DisplayName.Trim())),
            ]);

        // Longest first across both scopes together, so a library holding both
        // "Singapore" the country and "Singapore" the place, or "Hong Kong"
        // beside "Hong Kong Island", takes the more specific reading.
        (PlaceFilter? place, string? placeName, text) = TakeName(
            text,
            [
                .. places
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                    .Select(entry => (entry.Filter, Name: entry.Name.Trim())),
            ]);

        return new SearchTerms(personId, personName, place, placeName, text);
    }

    /// <summary>
    /// Takes the longest of these names that appears in the text, and returns
    /// what is left of the line.
    /// </summary>
    /// <remarks>
    /// Run once per directory rather than over both at once, so that a line
    /// naming a person and a place fills both slots - "Ana Lim sentosa" is their
    /// photographs on Sentosa, not their photographs with "sentosa" handed to the
    /// encoder as a description.
    /// </remarks>
    private static (T? Found, string? Name, string Remaining) TakeName<T>(
        string text, IReadOnlyList<(T Key, string Name)> candidates)
        where T : struct
    {
        foreach ((T key, string name) in candidates.OrderByDescending(entry => entry.Name.Length))
        {
            int at = IndexOfWholeWords(text, name);
            if (at >= 0)
            {
                return (
                    key,
                    name,
                    Collapse(string.Concat(text.AsSpan(0, at), " ", text.AsSpan(at + name.Length))));
            }
        }

        return (null, null, text);
    }

    /// <summary>
    /// Where <paramref name="word"/> sits in <paramref name="text"/> with a
    /// boundary either side of it, or -1.
    /// </summary>
    private static int IndexOfWholeWords(string text, string word)
    {
        if (word.Length == 0)
        {
            return -1;
        }

        int from = 0;
        while (from <= text.Length - word.Length)
        {
            int at = text.IndexOf(word, from, StringComparison.CurrentCultureIgnoreCase);
            if (at < 0)
            {
                return -1;
            }

            if (IsBoundary(text, at - 1) && IsBoundary(text, at + word.Length))
            {
                return at;
            }

            from = at + 1;
        }

        return -1;
    }

    /// <summary>
    /// Whether the character at a position ends a word - which the ends of the
    /// line do too.
    /// </summary>
    private static bool IsBoundary(string text, int index) =>
        index < 0 || index >= text.Length || !char.IsLetterOrDigit(text[index]);

    /// <summary>
    /// One space between words and none at either end, so that removing a name
    /// from the middle does not leave a gap the encoder has to make sense of.
    /// </summary>
    private static string Collapse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(
                ' ',
                text.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// How the split reads back to the person who typed it.
    /// </summary>
    /// <remarks>
    /// Every part that was understood, in the order the box treats them, so a
    /// line read as "Ana Lim · Sentosa · beach" shows all three and a line where
    /// the wrong word became a place shows that too.
    /// </remarks>
    public string Describe()
    {
        List<string> parts = [];

        if (PersonName is string who)
        {
            parts.Add(who);
        }

        if (PlaceName is string where)
        {
            parts.Add(where);
        }

        if (HasContent)
        {
            parts.Add(Content);
        }

        return string.Join(" · ", parts);
    }
}
