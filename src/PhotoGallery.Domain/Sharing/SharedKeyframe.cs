namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// One still taken out of a video, as another machine is told about it.
/// </summary>
/// <remarks>
/// <strong>No name.</strong> A photograph's rendition is named after a hash of
/// its bytes - which is exactly what the receiving machine is trying to avoid
/// reading - so it cannot name the file it wants and has to be told. A video's
/// frame is named from the path, the length, the modified time and the ordinal,
/// all of which that machine's own crawl already collected. So it computes the
/// name, and what travels is the part it cannot compute: how many frames there
/// are and where in the clip each was taken from.
///
/// <para>The position is not decoration. It is the only thing that explains a
/// frame to a person looking at it - a face found nine minutes in is a different
/// claim about a video than the same face on the poster.</para>
/// </remarks>
public sealed record SharedKeyframe(int Ordinal, TimeSpan Position);
