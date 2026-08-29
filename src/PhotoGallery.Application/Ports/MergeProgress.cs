namespace PhotoGallery.Application.Ports;

/// <summary>How far applying a merge has got.</summary>
public sealed record MergeProgress(string What, int Done, int Total);
