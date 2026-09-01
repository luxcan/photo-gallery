using PhotoGallery.Domain.People;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A machine with opinions, for tests about two of them disagreeing.
/// </summary>
/// <remarks>
/// The merge is a pure function of decision sets, so none of this needs a
/// working folder or a database - which is the whole reason the rules were
/// written that way. What it does need is for a test to read as the
/// disagreement it is about, rather than as nine lines of record construction.
/// </remarks>
internal sealed class Machine
{
    private readonly List<SharedPerson> _people = [];
    private readonly List<FaceAnswer> _answers = [];
    private readonly List<StrangerFace> _strangers = [];
    private readonly List<PhotoTurn> _turns = [];
    private readonly List<SharedAlbum> _albums = [];
    private readonly List<SharedAlbumMembership> _memberships = [];
    private readonly List<SharedAlbumRejection> _rejections = [];
    private readonly List<SharedEra> _eras = [];
    private readonly List<SharedSource> _sources;
    private readonly List<SourceLink> _links = [];

    public Machine(string name, Guid? id = null, int schemaVersion = 1, params Guid[] sources)
    {
        Identity = new MachineIdentity(id ?? Guid.NewGuid(), name, "1.0.0", schemaVersion);
        _sources =
        [
            .. (sources.Length == 0 ? [Share] : sources)
                .Select(source => new SharedSource(source, RootOf(source), 100)),
        ];
    }

    /// <summary>The one source every machine in these tests has in common.</summary>
    public static Guid Share { get; } = new("5ba4ed00-0000-4000-8000-000000000001");

    /// <summary>Names this machine's own folder, so pairing has something to compare.</summary>
    public Machine Keeps(Guid source, string root)
    {
        _sources.RemoveAll(held => held.SharedId == source);
        _sources.Add(new SharedSource(source, root, 100));
        return this;
    }

    /// <summary>Somebody confirmed that two folders are one.</summary>
    public Machine Pairs(Guid left, Guid right, DateTime when)
    {
        _links.Add(new SourceLink(left, right, when, Id));
        return this;
    }

    /// <summary>A default root, so two unrelated machines do not look like a pair.</summary>
    private static string RootOf(Guid source) => $@"\\house\{source:N}";

    public MachineIdentity Identity { get; }

    public Guid Id => Identity.Id;

    public Machine Knows(SharedPerson person)
    {
        _people.Add(person);
        return this;
    }

    public Machine Says(
        FaceKey face, Guid person, AssignmentSource source, DateTime when)
    {
        _answers.Add(new FaceAnswer(face, person, source, when, Id));
        return this;
    }

    public Machine Confirms(FaceKey face, Guid person, DateTime when) =>
        Says(face, person, AssignmentSource.Confirmed, when);

    public Machine Proposes(FaceKey face, Guid person, DateTime when) =>
        Says(face, person, AssignmentSource.Proposed, when);

    public Machine CallsNobody(FaceKey face, DateTime when)
    {
        _strangers.Add(new StrangerFace(face, when, Id));
        return this;
    }

    public Machine Turns(AssetKey photo, int rotation, DateTime when)
    {
        _turns.Add(new PhotoTurn(photo, rotation, when, Id));
        return this;
    }

    public Machine HasAlbum(SharedAlbum album)
    {
        _albums.Add(album);
        return this;
    }

    public Machine Puts(AssetKey photo, Guid album, DateTime when)
    {
        _memberships.Add(new SharedAlbumMembership(photo, album, when, Id));
        return this;
    }

    public Machine Refuses(AssetKey photo, string proposalKey, DateTime when)
    {
        _rejections.Add(new SharedAlbumRejection(photo, proposalKey, when, Id));
        return this;
    }

    public Machine Remembers(
        Guid person, DateTime fromUtc, DateTime toUtc, double degrees, int samples = 12)
    {
        _eras.Add(new SharedEra(person, fromUtc, toUtc, TestEmbeddings.At(degrees), samples));
        return this;
    }

    public DecisionSet Set(DateTime? writtenUtc = null) =>
        new(
            Identity,
            writtenUtc ?? new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
            _sources,
            _people,
            _answers,
            _strangers,
            _turns,
            _albums,
            _memberships,
            _rejections,
            _eras,
            _links);
}
