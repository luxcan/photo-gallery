namespace PhotoGallery.Application.Ports;

/// <summary>A published file this machine could not make sense of.</summary>
public sealed record UnreadableAnswers(string Name, string Problem);
