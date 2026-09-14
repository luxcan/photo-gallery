using Microsoft.EntityFrameworkCore;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Assets;

namespace PhotoGallery.Infrastructure.Persistence;

/// <summary>
/// Which photograph an album shows for itself, worked out in one place.
/// </summary>
/// <remarks>
/// The authority for it, for the reason <see cref="AssetDates"/> is the
/// authority for a capture date. The rule runs on every add, every remove and
/// now on every merge, and an album whose cover changed depending on which code
/// path last touched it would be worse than either answer on its own - which is
/// what a second copy of it in the merge would have arrived at.
///
/// <para>It lives here rather than in the domain because it is a query. The rule
/// is "the member with the most faces in it, or the middle of the span", and
/// both halves of that are questions only the database can answer.</para>
/// </remarks>
internal static class AlbumCovers
{
    /// <summary>
    /// Gives an album a cover if it has none, or has lost the one it had.
    /// </summary>
    /// <remarks>
    /// One with people in it, and the middle of the span only when there are
    /// none.
    ///
    /// <para>The row is left changed and unsaved, so a caller settling several
    /// albums at once writes them together.</para>
    /// </remarks>
    public static async Task EnsureAsync(
        GalleryDbContext db, int albumId, CancellationToken cancellationToken)
    {
        Album? album = await db.Albums
            .FirstOrDefaultAsync(row => row.Id == albumId, cancellationToken)
            .ConfigureAwait(false);

        if (album is null)
        {
            return;
        }

        List<int> members = await db.AlbumMembers
            .Where(member => member.AlbumId == albumId)
            .Join(
                db.Assets,
                member => member.AssetId,
                asset => asset.Id,
                (member, asset) => asset)

            // Not the capture date on its own: SQLite sorts a null first, so
            // every video and every undated photograph bunched at the front
            // and the middle of this list stopped being the middle of the
            // album's span. Videos only started arriving in albums in numbers
            // when a day rule learned to match them.
            .OrderBy(AssetDates.Taken)
            .Select(asset => asset.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (members.Count == 0)
        {
            album.CoverAssetId = 0;

            // Nothing is left to have chosen, so the next photograph to arrive
            // gets the rule rather than a decision about an empty album.
            album.CoverChosenUtc = null;
            return;
        }

        // A cover somebody chose is left exactly where it is, which is the whole
        // point of recording that they chose it: this runs on every add and
        // every remove, so without it the choice would survive until the next
        // photograph joined the album and would then be replaced in silence.
        if (album.CoverChosenUtc is not null && members.Contains(album.CoverAssetId))
        {
            return;
        }

        // The choice was about a photograph this album no longer holds - taken
        // out, or set aside as a duplicate. The rule is a better answer than a
        // picture that is not in here any more, and forgetting the choice is
        // what lets it be one again later.
        album.CoverChosenUtc = null;

        var withFaces = await db.Faces
            .AsNoTracking()
            .Where(face => members.Contains(face.AssetId))
            .GroupBy(face => face.AssetId)
            .Select(photo => new { AssetId = photo.Key, Faces = photo.Count() })
            .OrderByDescending(photo => photo.Faces)
            .ThenBy(photo => photo.AssetId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        album.CoverAssetId = withFaces?.AssetId ?? members[members.Count / 2];
    }
}
