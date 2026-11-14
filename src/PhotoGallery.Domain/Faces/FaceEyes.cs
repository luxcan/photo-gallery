namespace PhotoGallery.Domain.Faces;

/// <summary>
/// What was measured on one face for the keeper ladder of PRP 14: its eyes, and
/// how sharp it is.
/// </summary>
/// <remarks>
/// A table of its own rather than columns on <see cref="Face"/>, because a face
/// row is replaced whenever the face pass runs again and this measurement
/// belongs to that row; the cascade on delete keeps the two from ever
/// disagreeing. Read only by the "Looks the same" screen, never shared with
/// another computer - each works it out from the pixels it already has.
/// </remarks>
public sealed class FaceEyes
{
    /// <summary>The face, and the key: one measurement per face.</summary>
    public int FaceId { get; set; }

    public Face? Face { get; set; }

    /// <summary>
    /// Open, Closed or Unknown, or null when the eyes were not read because the
    /// eye model was not installed. Null is measured again once it is.
    /// </summary>
    public EyeState? State { get; set; }

    /// <summary>
    /// The wider eye's openness, kept for tuning. Closed needs both eyes at or
    /// below the limit, so this alone says how near the face came.
    /// </summary>
    public double? Openness { get; set; }

    /// <summary>
    /// The variance of the Laplacian over the face's box with the preview at the
    /// common size, or null when the preview is smaller than that size.
    /// </summary>
    public double? Sharpness { get; set; }

    public DateTime MeasuredUtc { get; set; }
}
