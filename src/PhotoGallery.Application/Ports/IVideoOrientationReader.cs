namespace PhotoGallery.Application.Ports;

/// <summary>
/// Reads which way up a clip's own file says it goes.
/// </summary>
/// <remarks>
/// A phone records a turned clip as an upright stream plus a note in the
/// container saying how to turn it. The Windows shell honours the note when it
/// makes the poster; the player does not, so a clip held upside down played
/// upside down under an upright poster - 141 clips in the first real library.
/// Guessing from the stream's shape against the poster can only ever see a
/// quarter turn, never a half one, which is why this asks the file.
/// </remarks>
public interface IVideoOrientationReader
{
    /// <summary>
    /// The quarter turn the file asks for and the size its picture is stored at,
    /// or null when the file cannot say.
    /// </summary>
    /// <remarks>
    /// Null covers every reason the answer is not known - a container that keeps
    /// no such note, a damaged file, one that is not a clip at all, and a share
    /// that is away. None of those is the same as "upright", which is a real
    /// answer of zero, so the caller falls back to guessing rather than trusting
    /// a default. Never throws for a file it cannot read.
    /// </remarks>
    VideoOrientation? Read(string fullPath);
}
