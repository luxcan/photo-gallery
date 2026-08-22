using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.People;

/// <summary>
/// One face and when its picture was taken.
/// </summary>
/// <remarks>
/// The date is required rather than optional. Grouping and eras both depend on
/// it entirely, and a face with no date at all cannot take part - so working out
/// the best answer available, from the photograph's own metadata or failing that
/// from the folder it sits in, happens before anything gets here.
/// </remarks>
public readonly record struct FaceSample(int FaceId, DateTime TakenUtc, FaceEmbedding Embedding);
