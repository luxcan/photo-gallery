using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Application.Ports;

/// <summary>What was measured on one face.</summary>
/// <param name="FaceId">The face row.</param>
/// <param name="State">The eye verdict, or null when the eyes were not read.</param>
/// <param name="Openness">The wider eye's openness, for tuning, or null.</param>
/// <param name="Sharpness">The face's sharpness at the common size, or null.</param>
public sealed record FaceMeasurement(int FaceId, EyeState? State, double? Openness, double? Sharpness);
