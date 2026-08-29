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
    IReadOnlyList<AlbumMembership> Memberships,
    IReadOnlyList<AlbumRejection> Rejections)
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
}
