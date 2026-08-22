namespace PhotoGallery.Application.Ports;

/// <summary>A face the user has said is not a particular person.</summary>
public sealed record FaceRejection(int FaceId, int PersonId);
