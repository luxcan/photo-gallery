using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Gallery;

/// <summary>
/// The quarter turn to draw a playing clip with: what its file asks for, plus
/// the turn the user saved.
/// </summary>
/// <remarks>
/// The player draws the stream as it is stored and ignores the file's own note,
/// so the note is added here. The user's turn is added on top, the same turn
/// that was made to the poster, so a clip turned once stays turned every time it
/// is played.
/// </remarks>
public static class PlaybackTurn
{
    /// <summary>The turn to draw the stream with, clockwise: 0, 90, 180 or 270.</summary>
    /// <param name="file">What the file says, or null when it cannot say.</param>
    /// <param name="savedTurn">The user's own turn, as recorded against the clip.</param>
    /// <param name="streamWidth">The width the player reports for the stream.</param>
    /// <param name="streamHeight">Its height.</param>
    /// <param name="stillIsPortrait">
    /// Whether the poster on screen is taller than wide, or null when it could
    /// not be measured. Only read when the file cannot say.
    /// </param>
    public static int For(
        VideoOrientation? file,
        int savedTurn,
        int streamWidth,
        int streamHeight,
        bool? stillIsPortrait)
    {
        int saved = Quarter(savedTurn);

        if (file is VideoOrientation told)
        {
            int fromFile = AlreadyApplied(told, streamWidth, streamHeight) ? 0 : told.Degrees;
            return Quarter(fromFile + saved);
        }

        return Quarter(Guessed(streamWidth, streamHeight, stillIsPortrait, saved) + saved);
    }

    /// <summary>
    /// Whether the player has turned the stream itself, which shows as the size
    /// it reports being the stored size the other way round.
    /// </summary>
    /// <remarks>
    /// Only a quarter turn shows in the shape. A half turn cannot be seen this
    /// way, and is taken as not applied, because the player on this machine has
    /// never been seen applying one.
    /// </remarks>
    private static bool AlreadyApplied(VideoOrientation told, int streamWidth, int streamHeight)
    {
        if (told.Degrees % 180 == 0
            || streamWidth <= 0 || streamHeight <= 0 || streamWidth == streamHeight
            || told.CodedWidth == told.CodedHeight)
        {
            return false;
        }

        return (streamHeight > streamWidth) != (told.CodedHeight > told.CodedWidth);
    }

    /// <summary>
    /// The turn the stream needs to match the poster as it was made, for a file
    /// that keeps no note - an .mts clip, or a damaged one.
    /// </summary>
    /// <remarks>
    /// The poster on screen already carries the user's turn, so it is turned back
    /// before comparing; comparing against it as drawn would count a saved
    /// quarter turn twice. Ninety rather than two-seventy when they disagree,
    /// because a phone held upright writes a quarter clockwise, which is nearly
    /// all of this library's portrait video.
    /// </remarks>
    private static int Guessed(int streamWidth, int streamHeight, bool? stillIsPortrait, int saved)
    {
        if (stillIsPortrait is not bool still || streamWidth <= 0 || streamHeight <= 0)
        {
            return 0;
        }

        bool madePortrait = saved % 180 == 90 ? !still : still;
        return (streamHeight > streamWidth) == madePortrait ? 0 : 90;
    }

    private static int Quarter(int degrees) => ((degrees % 360) + 360) % 360;
}
