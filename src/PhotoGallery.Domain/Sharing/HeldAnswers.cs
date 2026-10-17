namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Everything that arrived about photographs this library has not indexed yet,
/// or about faces it has not found yet.
/// </summary>
/// <remarks>
/// Kept rather than dropped, and this is the single most important merge rule in
/// the feature: without it the order of operations becomes something the user
/// has to get right - scan first, then share, and if you did it the other way
/// round you silently lost an evening's work. With it, the order does not matter
/// and cannot be got wrong.
///
/// <para>Typed rather than serialised here. Turning one of these into a row is
/// the business of whatever writes rows, and a domain that knew about JSON would
/// be a domain that had learned something about storage.</para>
/// </remarks>
public sealed record HeldAnswers(
    IReadOnlyList<FaceAnswer> Answers,
    IReadOnlyList<StrangerFace> Strangers,
    IReadOnlyList<PhotoTurn> Turns,
    IReadOnlyList<SharedAlbumMembership> Memberships,
    IReadOnlyList<SharedAlbumRejection> Rejections)
{
    public static HeldAnswers None { get; } = new([], [], [], [], []);

    /// <summary>How many answers are waiting, which is what the screen says.</summary>
    public int Count =>
        Answers.Count + Strangers.Count + Turns.Count + Memberships.Count + Rejections.Count;

    /// <summary>The photographs they are waiting for.</summary>
    public IReadOnlyCollection<AssetKey> Photographs =>
        new HashSet<AssetKey>(
        [
            .. Answers.Select(a => a.Face.Photo),
            .. Strangers.Select(s => s.Face.Photo),
            .. Turns.Select(t => t.Photo),
            .. Memberships.Select(m => m.Photo),
            .. Rejections.Select(r => r.Photo),
        ]);

    /// <summary>The same answers with everything in <paramref name="other"/> taken out.</summary>
    /// <remarks>
    /// How a sweep works out what it can forget: everything that was waiting,
    /// less everything it is keeping. That is <em>not</em> the same as what it
    /// landed, and treating the two as one was a bug - an answer this library
    /// already held, or one a later answer had beaten, was in neither list and
    /// so was counted as applied having written nothing. The merge says which
    /// answers it settled without a change; what landed is what is left after
    /// those are taken out too.
    /// </remarks>
    public HeldAnswers Except(HeldAnswers other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new HeldAnswers(
            [.. Answers.Except(other.Answers)],
            [.. Strangers.Except(other.Strangers)],
            [.. Turns.Except(other.Turns)],
            [.. Memberships.Except(other.Memberships)],
            [.. Rejections.Except(other.Rejections)]);
    }

    /// <summary>
    /// The same answers, with a folder called by the identity a pairing settled
    /// it on.
    /// </summary>
    /// <remarks>
    /// The answer is what a sweep reads, so it has to say the new name as well
    /// as the row it is stored in. Renaming only the row left every answer
    /// inside naming the folder the old way: the sweep looked for a photograph
    /// this library no longer calls that, never found it, and parked the answer
    /// back under the old name beside the renamed one.
    /// </remarks>
    public HeldAnswers Renamed(Guid from, Guid to)
    {
        return new HeldAnswers(
            [.. Answers.Select(a => a with { Face = a.Face with { Photo = As(a.Face.Photo) } })],
            [.. Strangers.Select(s => s with { Face = s.Face with { Photo = As(s.Face.Photo) } })],
            [.. Turns.Select(t => t with { Photo = As(t.Photo) })],
            [.. Memberships.Select(m => m with { Photo = As(m.Photo) })],
            [.. Rejections.Select(r => r with { Photo = As(r.Photo) })]);

        AssetKey As(AssetKey photo) =>
            photo.SharedSourceId == from ? new AssetKey(to, photo.RelativePath) : photo;
    }
}
