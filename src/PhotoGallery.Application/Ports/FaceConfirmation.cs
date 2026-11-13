namespace PhotoGallery.Application.Ports;

/// <summary>A face the user has said is a particular person.</summary>
public sealed record FaceConfirmation(int FaceId, int PersonId);
