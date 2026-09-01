using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Faces;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>Shorthand for the things a decision points at.</summary>
internal static class Pictures
{
    public static AssetKey Photo(string relativePath, Guid? source = null) =>
        new(source ?? Machine.Share, relativePath);

    public static FaceKey Face(string relativePath, int x = 10, int y = 10, int size = 40) =>
        new(Photo(relativePath), new FaceBounds(x, y, size, size));

    /// <summary>A library that has indexed these photographs and found these faces.</summary>
    public static LibraryContents Holding(params FaceKey[] faces)
    {
        Dictionary<AssetKey, IReadOnlyList<FaceBounds>> found = [];

        foreach (FaceKey face in faces)
        {
            if (!found.TryGetValue(face.Photo, out IReadOnlyList<FaceBounds>? boxes))
            {
                found[face.Photo] = boxes = new List<FaceBounds>();
            }

            ((List<FaceBounds>)boxes).Add(face.Bounds);
        }

        return new LibraryContents(
            new HashSet<Guid> { Machine.Share },
            new HashSet<AssetKey>(found.Keys),
            found);
    }

    /// <summary>A library that has indexed these photographs but found no faces in them.</summary>
    public static LibraryContents Indexed(params AssetKey[] photographs) =>
        new(
            new HashSet<Guid> { Machine.Share },
            new HashSet<AssetKey>(photographs),
            new Dictionary<AssetKey, IReadOnlyList<FaceBounds>>());

    /// <summary>An album somebody made, with a name they typed.</summary>
    public static SharedAlbum Album(Guid id, string name, DateTime namedUtc) =>
        new(id, name, AlbumOrigin.Made, null, namedUtc, null);

    /// <summary>One the app proposed, named after the run of days it covers.</summary>
    public static SharedAlbum Proposal(string proposalKey, string name, DateTime? namedUtc = null) =>
        new(
            Guid.NewGuid(),
            name,
            namedUtc is null ? AlbumOrigin.Proposed : AlbumOrigin.Accepted,
            proposalKey,
            namedUtc,
            null);
}
