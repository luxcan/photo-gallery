using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.Sharing;

/// <summary>What one person looked like over one stretch of time.</summary>
/// <remarks>
/// The one deliberate exception to sending only what cannot be re-derived. An
/// era is the mean of the confirmed faces in a stretch of time, so a machine
/// rebuilds it from what it holds - but it can only hold confirmations about
/// photographs it can see. Where somebody has named a person in two hundred
/// pictures and half of them live only on their own laptop, the other machines
/// rebuild a weaker centroid and propose worse. Fifty kilobytes closes that.
///
/// <para><strong>A seed, not a fact.</strong> Eras are rebuilt locally after
/// every merge exactly as they are today, and a received centroid is kept only
/// where the rebuild produces nothing for that person and that stretch of time.
/// The first local confirmation in that era replaces it.</para>
/// </remarks>
public sealed record SharedEra(
    Guid Person,
    DateTime FromUtc,
    DateTime ToUtc,
    FaceEmbedding Centroid,
    int SampleCount);
