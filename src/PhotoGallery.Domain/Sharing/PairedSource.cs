namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// A confirmed pairing, as this library keeps it.
/// </summary>
/// <remarks>
/// The stored twin of <see cref="SourceLink"/>, which is the shape the merge
/// works in - the same split as <see cref="SharedPerson"/> against a person, and
/// for the same reason: a row has an id and a tracked identity, and a decision
/// has neither.
///
/// <para>Kept for ever. A pairing is small, there is no way to say two folders
/// are <em>not</em> one, and a link forgotten is a link every other machine
/// would hand straight back on the next share.</para>
/// </remarks>
public sealed class PairedSource
{
    public int Id { get; set; }

    /// <summary>The lower of the two shared ids. See <see cref="SourceLink.Ordered"/>.</summary>
    public Guid Left { get; set; }

    public Guid Right { get; set; }

    /// <summary>When somebody confirmed it - not when this library heard.</summary>
    public DateTime PairedUtc { get; set; }

    /// <summary>The machine it was confirmed on, kept so a forwarded link says who.</summary>
    public Guid DecidedBy { get; set; }

    public SourceLink AsLink() => new(Left, Right, PairedUtc, DecidedBy);
}
