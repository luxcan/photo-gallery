using Microsoft.Data.Sqlite;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Measure;

/// <summary>
/// Reads the duplicate sets and known faces out of a copy of the library's
/// database, read-only.
/// </summary>
/// <remarks>
/// Plain SQL over the copy rather than the app's DbContext: the context would
/// want to migrate what it opens, and this must never write to anything.
/// Enum values are the stored integers - <c>DuplicateKind</c> Exact 0 and Near
/// 1, <c>DuplicateRole</c> Keeper 0, Redundant 1 and Undecided 2, <c>AssignmentSource</c>
/// Proposed 0 and Confirmed 1.
/// </remarks>
internal sealed class LibraryCopy : IDisposable
{
    private readonly SqliteConnection _connection;

    public LibraryCopy(string databasePath)
    {
        _connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        _connection.Open();
    }

    /// <summary>How many exact sets there are and how many copies they hold. Never measured.</summary>
    public (int Sets, int Copies) CountExact()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(DISTINCT s.Id), COUNT(m.Id)
            FROM DuplicateSets s JOIN DuplicateMembers m ON m.DuplicateSetId = s.Id
            WHERE s.Kind = 0
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    /// <summary>Every copy in every near set, resolved or not.</summary>
    public IReadOnlyList<NearCopy> ReadNearCopies()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT s.Id, s.IsResolved, a.Id, p.Path, a.RelativePath, a.Width, a.Height, a.Length,
                   a.TakenUtc IS NOT NULL, a.Latitude IS NOT NULL AND a.Longitude IS NOT NULL,
                   m.Role, a.QuarantinedUtc IS NOT NULL, a.ThumbnailName
            FROM DuplicateSets s
            JOIN DuplicateMembers m ON m.DuplicateSetId = s.Id
            JOIN Assets a ON a.Id = m.AssetId
            JOIN PhotoSources p ON p.Id = a.PhotoSourceId
            WHERE s.Kind = 1
            ORDER BY s.Id, a.Id
            """;

        var copies = new List<NearCopy>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            copies.Add(new NearCopy(
                reader.GetInt32(0),
                reader.GetBoolean(1),
                reader.GetInt32(2),
                Path.Combine(reader.GetString(3), reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetInt64(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetInt32(10) switch { 0 => "Keeper", 1 => "Redundant", _ => "Undecided" },
                reader.GetBoolean(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return copies;
    }

    /// <summary>
    /// The known faces on the given photographs: confirmed or proposed to a
    /// named person still in the library, and not ignored.
    /// </summary>
    /// <remarks>
    /// A face carries at most one live name in practice, but should two rows
    /// ever name it, the confirmed answer is the one a person gave.
    /// </remarks>
    public IReadOnlyList<KnownFace> ReadKnownFaces(IReadOnlySet<int> assetIds)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT f.Id, f.AssetId, f.BoundsX, f.BoundsY, f.BoundsWidth, f.BoundsHeight, f.DetectScore,
                   fa.PersonId, p.DisplayName, fa.Source
            FROM Faces f
            JOIN FaceAssignments fa ON fa.FaceId = f.Id AND fa.Source IN (0, 1)
            JOIN People p ON p.Id = fa.PersonId AND p.DeletedUtc IS NULL
            WHERE f.IgnoredUtc IS NULL
            ORDER BY f.AssetId, f.Id, fa.Source DESC
            """;

        var faces = new List<KnownFace>();
        int lastFace = -1;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int faceId = reader.GetInt32(0);
            int assetId = reader.GetInt32(1);
            if (faceId == lastFace || !assetIds.Contains(assetId))
            {
                continue;
            }

            lastFace = faceId;
            faces.Add(new KnownFace(
                faceId,
                assetId,
                new FaceBounds(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)),
                reader.GetFloat(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.GetInt32(9) == 1 ? "Confirmed" : "Proposed"));
        }

        return faces;
    }

    public void Dispose() => _connection.Dispose();
}
