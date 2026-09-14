using PhotoGallery.Application.Ports;

namespace PhotoGallery.App.Albums;

/// <summary>
/// What to say after photographs have been put in an album.
/// </summary>
/// <remarks>
/// One wording, because a photograph belongs to one album and so putting it
/// somewhere takes it out of wherever it was. That rule is nobody's expectation
/// until it happens to them, so it is said out loud every time rather than
/// applied quietly - and a sentence written twice is a sentence that will
/// eventually say two different things about the same act.
///
/// <para>The album is named because the reader may not be looking at it: the
/// wall they are standing on is the one the photographs just left.</para>
/// </remarks>
internal static class AlbumMoveNotice
{
    /// <param name="album">The album they went into.</param>
    /// <param name="placed">How many were asked for.</param>
    /// <param name="result">What the write reported, including where they came from.</param>
    public static string For(string album, int placed, AlbumAddResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (placed == 0)
        {
            return string.Empty;
        }

        string count = placed == 1 ? "1 photograph" : $"{placed:N0} photographs";

        if (result.Moved == 0 || result.From.Count == 0)
        {
            return placed == 1
                ? $"Added to {album}"
                : $"{count} added to {album}.";
        }

        string from = string.Join(" and ", result.From);

        if (placed == 1)
        {
            return $"Moved into {album}, out of {from}";
        }

        // The second count only where it differs, so the ordinary case - a whole
        // batch out of one album - reads as one fact rather than two numbers to
        // reconcile.
        return result.Moved == placed
            ? $"{count} moved into {album}, out of {from}."
            : $"{count} added to {album}, {result.Moved:N0} of them out of {from}.";
    }
}
