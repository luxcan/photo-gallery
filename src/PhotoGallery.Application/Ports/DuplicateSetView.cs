using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Application.Ports;

/// <summary>One copy of a duplicated picture, as the review screen shows it.</summary>
/// <param name="Distance">
/// How many bits of the perceptual hash differ from the copy being kept. Zero
/// for an exact set and for the keeper itself; a small number for a near one is
/// how alike the two are.
/// </param>
/// <param name="ContentHash">
/// The SHA-256 of the whole file. Shown in the detail view because for an
/// identical set it is the entire argument: two files with the same digest are
/// the same file, and there is nothing to choose between them but the name.
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
    DateTime ModifiedUtc = default);

/// <summary>A group of copies of the same picture, with one of them chosen to stay.</summary>
public sealed record DuplicateSetView(
    int Id,
    DuplicateKind Kind,
    IReadOnlyList<DuplicateCopy> Copies)
{
    public DuplicateCopy? Keeper =>
        Copies.FirstOrDefault(copy => copy.Role == DuplicateRole.Keeper);

    public IReadOnlyList<DuplicateCopy> Redundant =>
        [.. Copies.Where(copy => copy.Role == DuplicateRole.Redundant)];

    /// <summary>Bytes that would come back if the redundant copies were set aside.</summary>
    public long RedundantBytes => Redundant.Sum(copy => copy.Length);
}
