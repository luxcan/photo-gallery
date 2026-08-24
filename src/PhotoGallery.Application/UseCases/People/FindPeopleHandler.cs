using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.People;

/// <summary>
/// Answers the gallery's search box: which named people match what has been
/// typed so far.
/// </summary>
/// <remarks>
/// Names only, and deliberately so. Searching by what is *in* a picture is
/// feature 07 and needs another 600 MB of weights; searching by who is in it
/// needs nothing that has not already been worked out, and it is the question
/// this app was built to answer.
/// </remarks>
public sealed class FindPeopleHandler
{
    /// <summary>How many names the box offers at once.</summary>
    public const int MaxMatches = 8;

    private readonly IPeopleReader _people;

    public FindPeopleHandler(IPeopleReader people) => _people = people;

    /// <summary>
    /// People whose name contains what was typed, best first. An empty search
    /// offers everyone, so an empty box still answers "who can I look for?".
    /// </summary>
    /// <remarks>
    /// A name that starts with the text beats one that merely contains it, and
    /// among equals the person with more pictures comes first - typing "j" for
    /// a library holding Ana Lim and a Jane photographed once should not lead
    /// with Jane.
    /// </remarks>
    public async Task<IReadOnlyList<PersonDirectoryEntry>> HandleAsync(
        string? search, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PersonDirectoryEntry> everyone =
            await _people.GetDirectoryAsync(cancellationToken).ConfigureAwait(false);

        string text = search?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return [.. everyone.OrderByDescending(entry => entry.Photos).Take(MaxMatches)];
        }

        return
        [
            .. everyone
                .Where(entry => entry.DisplayName.Contains(
                    text, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(entry => entry.DisplayName.StartsWith(
                    text, StringComparison.CurrentCultureIgnoreCase))
                .ThenByDescending(entry => entry.Photos)
                .ThenBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxMatches),
        ];
    }
}
