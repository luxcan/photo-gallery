namespace PhotoGallery.Application.Ports;

/// <summary>
/// One file as the walker found it: path relative to the source root, plus the
/// facts that reveal whether it has changed since the last scan.
/// </summary>
/// <param name="CreatedUtc">
/// When the file appeared at this location - which for a copied archive is the
/// day it was copied, not the day it was taken.
/// </param>
public readonly record struct ScannedFile(
    string RelativePath, long Length, DateTime ModifiedUtc, DateTime CreatedUtc);
