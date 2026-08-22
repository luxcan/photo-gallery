using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Duplicates;

namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>Everything the duplicates screen shows.</summary>
public sealed class GetDuplicatesHandler
{
    private readonly IDuplicateRepository _duplicates;

    public GetDuplicatesHandler(IDuplicateRepository duplicates) => _duplicates = duplicates;

    public async Task<DuplicateBoard> HandleAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DuplicateSetView> exact =
            await _duplicates.GetAsync(DuplicateKind.Exact, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<DuplicateSetView> near =
            await _duplicates.GetAsync(DuplicateKind.Near, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<QuarantinedCopy> setAside =
            await _duplicates.GetQuarantinedAsync(cancellationToken).ConfigureAwait(false);

        return new DuplicateBoard(exact, near, setAside);
    }
}

/// <summary>
/// The two kinds, kept apart, and what has already been set aside.
/// </summary>
/// <remarks>
/// Never one merged list. Byte-identical is a proof and can be approved in bulk;
/// visually alike is a question, because a perceptual hash cannot tell a
/// re-saved copy from the next frame of a burst - and those bursts are often
/// photographs worth keeping.
/// </remarks>
public sealed record DuplicateBoard(
    IReadOnlyList<DuplicateSetView> Exact,
    IReadOnlyList<DuplicateSetView> Near,
    IReadOnlyList<QuarantinedCopy> SetAside)
{
    public long ExactBytes => Exact.Sum(set => set.RedundantBytes);

    public long NearBytes => Near.Sum(set => set.RedundantBytes);

    public long SetAsideBytes => SetAside.Sum(copy => copy.Length);

    public bool HasAnything => Exact.Count + Near.Count > 0;
}
