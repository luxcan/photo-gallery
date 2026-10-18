using System.IO.Compression;
using System.Text.Json;
using PhotoGallery.Domain.Albums;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// What a file this release writes looks like to the release before it, and
/// what a file that release wrote looks like here.
/// </summary>
/// <remarks>
/// Every laptop in the house is updated by hand, so for a while some of them
/// run the previous release and read the same folder. Nothing this release
/// writes may make that one fail - and the one way it could, a deleted
/// suggestion that breaks its scans, is kept out of its sight. Read the other
/// way, nothing that release leaves out may be mistaken for an answer.
/// </remarks>
public sealed class PreviousReleaseFileTests
{
    private static readonly DateTime Monday = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A deleted suggestion is never shown to the previous release.
    /// </summary>
    /// <remarks>
    /// That release reads a deleted album carrying a run of days as "turn my own
    /// suggestion for these days into a tombstone", and its next scan then adds
    /// a second row under a key the tombstone still holds - which the database
    /// refuses, on every scan from then on. So in the file such an album sits in
    /// a list of its own, which that release does not know and skips, and this
    /// one puts back on the way in. A deleted album somebody made has no run of
    /// days and stays where it always was.
    /// </remarks>
    [Fact]
    public async Task ADeletedSuggestionIsNeverShownToThePreviousRelease()
    {
        DecisionSet before = WithEveryKindOfAlbum();

        using var stream = new MemoryStream();
        await DecisionSetFile.WriteAsync(stream, before);

        using JsonDocument file = JsonDocument.Parse(Gunzipped(stream));
        JsonElement albums = file.RootElement.GetProperty("Albums");

        Assert.DoesNotContain(
            albums.EnumerateArray(),
            album => album.TryGetProperty("ProposalKey", out _)
                  && album.TryGetProperty("DeletedUtc", out _));

        Assert.Equal(2, file.RootElement.GetProperty("DeletedSuggestions").GetArrayLength());

        Assert.Contains(
            albums.EnumerateArray(),
            album => album.GetProperty("Name").GetString() == "Genting"
                  && album.TryGetProperty("DeletedUtc", out _));

        stream.Position = 0;
        DecisionSet after = await DecisionSetFile.ReadAsync(stream);

        Assert.Equal(
            before.Albums.OrderBy(album => album.Name, StringComparer.Ordinal),
            after.Albums.OrderBy(album => album.Name, StringComparer.Ordinal));
        Assert.Empty(after.DeletedSuggestions);
        Assert.Equal(before.Dismissals, after.Dismissals);
    }

    /// <summary>
    /// The previous release reads what this one writes without fault, and finds
    /// no deleted suggestion in it.
    /// </summary>
    /// <remarks>
    /// Read the way that release reads it: the same serialiser settings, into
    /// the shapes it had, which know nothing of when a suggestion was kept, of
    /// dismissals, or of the list deleted suggestions are set aside in. An
    /// unknown property is skipped. An origin it has no name for would make the
    /// whole file unreadable there, so every origin has to be one it knows.
    /// </remarks>
    [Fact]
    public async Task ThePreviousReleaseReadsThisFileWithoutFault()
    {
        using var stream = new MemoryStream();
        await DecisionSetFile.WriteAsync(stream, WithEveryKindOfAlbum());

        PreviousDecisionSet? read =
            JsonSerializer.Deserialize<PreviousDecisionSet>(Gunzipped(stream), DecisionSetFile.Shape);

        Assert.NotNull(read);
        Assert.All(
            read.Albums,
            album => Assert.Contains(
                album.Origin, new[] { AlbumOrigin.Proposed, AlbumOrigin.Accepted, AlbumOrigin.Made }));
        Assert.DoesNotContain(
            read.Albums, album => album.ProposalKey is not null && album.DeletedUtc is not null);

        // The deleted album somebody made, and the kept one that is still here.
        Assert.Equal(2, read.Albums.Count);
    }

    /// <summary>
    /// A file from the previous release reads as keeps that cannot say when,
    /// and no dismissals.
    /// </summary>
    /// <remarks>
    /// Written out by hand in that release's shape, so the claim does not rest
    /// on this release's own writer. Neither absence is an answer: a keep
    /// that cannot say when is still a keep, and a release that recorded no
    /// dismissals really has none to tell. Its deleted suggestions are where it
    /// always put them, and are read from there.
    /// </remarks>
    [Fact]
    public async Task AFileFromThePreviousReleaseReadsAsUndatedKeepsAndNoDismissals()
    {
        const string written = """
            {
              "Machine": {
                "Id": "11111111-0000-4000-8000-000000000001",
                "Name": "Mum's laptop",
                "AppVersion": "1.0.0",
                "SchemaVersion": 1
              },
              "WrittenUtc": "2026-03-05T00:00:00Z",
              "Sources": [],
              "People": [],
              "Answers": [],
              "Strangers": [],
              "Turns": [],
              "Albums": [
                {
                  "PublicId": "22222222-0000-4000-8000-000000000002",
                  "Name": "March 2019",
                  "Origin": "Accepted",
                  "ProposalKey": "2019-03-03..2019-03-05"
                },
                {
                  "PublicId": "33333333-0000-4000-8000-000000000003",
                  "Name": "April 2019",
                  "Origin": "Accepted",
                  "ProposalKey": "2019-04-03..2019-04-05",
                  "DeletedUtc": "2026-03-03T09:00:00Z"
                }
              ],
              "Memberships": [],
              "Rejections": [],
              "Eras": [],
              "Links": [],
              "Collections": []
            }
            """;

        DecisionSet read = await DecisionSetFile.ReadAsync(Gzipped(written));

        SharedAlbum kept = read.Albums.Single(album => album.DeletedUtc is null);
        Assert.Equal(AlbumOrigin.Accepted, kept.Origin);
        Assert.Null(kept.KeptUtc);

        Assert.Empty(read.Dismissals);
        Assert.Empty(read.DeletedSuggestions);
        Assert.Contains(
            read.Albums,
            album => album.ProposalKey == "2019-04-03..2019-04-05" && album.DeletedUtc is not null);
    }

    /// <summary>
    /// A kept suggestion and a suggestion both deleted, an album somebody made
    /// and deleted, a kept suggestion still here, and a dismissal.
    /// </summary>
    private static DecisionSet WithEveryKindOfAlbum()
    {
        DateTime tuesday = Monday.AddDays(1);

        return new Machine("Mum")
            .HasAlbum(Pictures.Kept("2019-03-03..2019-03-05", "March 2019", Monday) with
            {
                DeletedUtc = tuesday,
            })
            .HasAlbum(Pictures.Proposal("2019-04-03..2019-04-05", "April 2019") with
            {
                DeletedUtc = tuesday,
            })
            .HasAlbum(Pictures.Album(Guid.NewGuid(), "Genting", Monday) with { DeletedUtc = tuesday })
            .HasAlbum(Pictures.Kept("2019-05-03..2019-05-05", "May 2019", Monday))
            .Dismisses("2019-06-03..2019-06-05", tuesday)
            .Set();
    }

    private static string Gunzipped(MemoryStream written)
    {
        written.Position = 0;

        using var gzip = new GZipStream(written, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    private static MemoryStream Gzipped(string json)
    {
        var stream = new MemoryStream();

        using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(gzip))
        {
            writer.Write(json);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>An album, as the previous release reads one.</summary>
    private sealed record PreviousSharedAlbum(
        Guid PublicId,
        string Name,
        AlbumOrigin Origin,
        string? ProposalKey,
        DateTime? NamedUtc,
        DateTime? DeletedUtc,
        Guid? Shelf = null,
        DateTime? ShelvedUtc = null,
        AssetKey? Cover = null,
        DateTime? CoverChosenUtc = null);

    /// <summary>A decision set, as the previous release reads one.</summary>
    private sealed record PreviousDecisionSet(
        MachineIdentity Machine,
        DateTime WrittenUtc,
        IReadOnlyList<SharedSource> Sources,
        IReadOnlyList<SharedPerson> People,
        IReadOnlyList<FaceAnswer> Answers,
        IReadOnlyList<StrangerFace> Strangers,
        IReadOnlyList<PhotoTurn> Turns,
        IReadOnlyList<PreviousSharedAlbum> Albums,
        IReadOnlyList<SharedAlbumMembership> Memberships,
        IReadOnlyList<SharedAlbumRejection> Rejections,
        IReadOnlyList<SharedEra> Eras,
        IReadOnlyList<SourceLink> Links)
    {
        public IReadOnlyList<SharedCollection> Collections { get; init; } = [];
    }
}
