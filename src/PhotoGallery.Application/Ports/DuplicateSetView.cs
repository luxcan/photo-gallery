using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Application.Ports;

/// <summary>One copy of a duplicated picture, as the review screen shows it.</summary>
/// <param name="Distance">
/// How many bits of the perceptual hash differ from the copy suggested, or from
/// the set's first copy when none is. Zero for an identical set.
/// </param>
/// <param name="ContentHash">
/// The SHA-256 of the whole file. Shown in the detail view because for an
/// identical set it is the entire argument: two files with the same digest are
/// the same file, and there is nothing to choose between them but the name.
/// </param>
/// <param name="DecidedBy">
/// For the suggested copy, the rung of the keeper ladder that chose it; for a
/// copy beaten, the rung it lost on. Null when the ladder said nothing about it.
/// </param>
/// <param name="Exposure">Which way it is wrong, when it lost on exposure.</param>
/// <param name="ClosedFaces">
/// The known faces counted against it on eyes, for the caption that names them
/// and the marker drawn over each.
/// </param>
/// <param name="PreviewWidth">
/// The width of the preview the face boxes are measured in, so a marker can be
/// placed over the tile; zero when the copy was never measured.
/// </param>
/// <param name="PreviewHeight">The height of that preview, or zero.</param>
/// <param name="Fingerprint">
/// Its perceptual hash, so <paramref name="Distance"/> can be measured again from
/// whichever copy the screen suggests.
/// </param>
public sealed record DuplicateCopy(
    int AssetId,
    int PhotoSourceId,
    string RelativePath,
    string FullPath,
    string? ThumbnailName,
    long Length,
    int? Width,
    int? Height,
    DuplicateRole Role,
    int Distance,
    string? ContentHash = null,
    DateTime? TakenUtc = null,
    DateTime ModifiedUtc = default,
    KeeperCriterion? DecidedBy = null,
    ExposureFault Exposure = ExposureFault.None,
    IReadOnlyList<CopyFace>? ClosedFaces = null,
    int PreviewWidth = 0,
    int PreviewHeight = 0,
    PerceptualHash? Fingerprint = null)
{
    /// <summary>The known people whose closed eyes cost this copy, each named once.</summary>
    public IReadOnlyList<string> ClosedNames =>
        [.. (ClosedFaces ?? []).Select(face => face.PersonName).Distinct(StringComparer.CurrentCulture)];
}

/// <summary>
/// A group of copies of the same picture, with the one the app suggests
/// keeping, if any.
/// </summary>
public sealed record DuplicateSetView(
    int Id,
    DuplicateKind Kind,
    IReadOnlyList<DuplicateCopy> Copies)
{
    public DuplicateCopy? Keeper =>
        Copies.FirstOrDefault(copy => copy.Role == DuplicateRole.Keeper);

    public IReadOnlyList<DuplicateCopy> Redundant =>
        [.. Copies.Where(copy => copy.Role == DuplicateRole.Redundant)];

    /// <summary>The rung that chose the suggested copy, or null when nothing is suggested.</summary>
    public KeeperCriterion? DecidedBy => Keeper?.DecidedBy;

    /// <summary>
    /// Bytes that would come back if every copy but one were set aside: the
    /// redundant ones when a copy is suggested, and all but the largest when not.
    /// </summary>
    /// <remarks>
    /// Used to put the biggest saving first, so a set nothing was suggested in
    /// still sorts by what deciding it would be worth.
    /// </remarks>
    public long RedundantBytes => Keeper is null
        ? Copies.Sum(copy => copy.Length) - Copies.Select(copy => copy.Length).DefaultIfEmpty().Max()
        : Redundant.Sum(copy => copy.Length);
}
