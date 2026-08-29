namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// That two shared ids are the same folder, reached two ways.
/// </summary>
/// <remarks>
/// A decision like any other, published and merged, and that is what makes it
/// work at all. One person confirms the pair on one laptop; every other machine
/// takes the link on its next share and rewrites its own source to match. A
/// pairing that lived only where it was made would leave the house half paired
/// for ever, with nothing on any screen to say so.
///
/// <para><strong>The lower id wins, on every machine.</strong> Not the confirming
/// machine's, and not the older one: two people can confirm the same pair at the
/// same moment on two laptops, and any rule that depends on who asked first ends
/// with the two of them swapping ids and never settling. Sorting two
/// <see cref="Guid"/> values is a rule every machine reaches alone and reaches
/// the same.</para>
/// </remarks>
public sealed record SourceLink(Guid Left, Guid Right, DateTime PairedUtc, Guid DecidedBy)
{
    /// <summary>The id both sources end up sharing.</summary>
    public Guid Canonical => Left.CompareTo(Right) <= 0 ? Left : Right;

    /// <summary>The id that gives way to it.</summary>
    public Guid Absorbed => Left.CompareTo(Right) <= 0 ? Right : Left;

    /// <summary>The same link written the one way round, so two of them compare.</summary>
    public SourceLink Ordered() =>
        Left.CompareTo(Right) <= 0 ? this : new SourceLink(Right, Left, PairedUtc, DecidedBy);
}
