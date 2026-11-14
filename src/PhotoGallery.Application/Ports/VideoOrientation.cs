namespace PhotoGallery.Application.Ports;

/// <summary>Which way up a clip's file says it goes.</summary>
/// <param name="Degrees">The quarter turn to draw it with, clockwise: 0, 90, 180 or 270.</param>
/// <param name="CodedWidth">The stored picture's width, before that turn.</param>
/// <param name="CodedHeight">Its height, before that turn.</param>
/// <remarks>
/// The size before the turn is kept so the caller can tell whether the player
/// has already applied it: a player that honours the note reports the size
/// swapped, and turning that picture again would put it on its side.
/// </remarks>
public readonly record struct VideoOrientation(int Degrees, int CodedWidth, int CodedHeight);
