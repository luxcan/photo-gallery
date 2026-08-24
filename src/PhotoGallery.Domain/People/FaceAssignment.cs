using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Domain.People;

/// <summary>Links a detected face to a named person.</summary>
public sealed class FaceAssignment
{
    public int Id { get; set; }

    public int FaceId { get; set; }

    public Face? Face { get; set; }

    public int PersonId { get; set; }

    public Person? Person { get; set; }

    public AssignmentSource Source { get; set; }

    /// <summary>Similarity that produced a proposal. Unset for manual assignments.</summary>
    public float? Score { get; set; }
}
