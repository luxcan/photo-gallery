namespace PhotoGallery.Application.Ports;

/// <summary>
/// A face being assigned, and how sure the app was.
/// </summary>
/// <param name="Score">
/// Unset when a person said so themselves. A confirmation is not a measurement,
/// and recording one against it would invite treating the two the same.
/// </param>
public sealed record ScoredFace(int FaceId, float? Score = null);
