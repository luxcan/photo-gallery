using System.Globalization;

namespace PhotoGallery.Domain.Albums;

/// <summary>
/// What a refusal of a photograph is remembered under, for an album somebody
/// made.
/// </summary>
/// <remarks>
/// A suggestion is remembered by its run of days; an album somebody made by its
/// public id, which is the same on every computer. It used to be the album's row
/// number, which means nothing on another laptop - so a refusal travelled to a
/// different album there, or to none, and could keep a photograph out of an
/// unrelated album's "Find photos that fit". Keys in that old form may still
/// arrive from a laptop on an older build, and are recognised so they can be
/// left out.
/// </remarks>
public static class AlbumRefusalKey
{
    private const string MadePrefix = "made:";

    /// <summary>The key for refusals of photographs from an album somebody made.</summary>
    public static string Made(Guid publicId) =>
        string.Create(CultureInfo.InvariantCulture, $"{MadePrefix}{publicId:D}");

    /// <summary>
    /// Whether a key is in the old form that named an album by its row number,
    /// which only ever meant something on the computer that wrote it.
    /// </summary>
    public static bool IsLocalOnly(string proposalKey)
    {
        ArgumentNullException.ThrowIfNull(proposalKey);

        return proposalKey.StartsWith(MadePrefix, StringComparison.Ordinal)
            && int.TryParse(
                proposalKey.AsSpan(MadePrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _);
    }
}
