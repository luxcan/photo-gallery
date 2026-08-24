namespace PhotoGallery.Domain.Faces;

/// <summary>
/// Where a face sits in its photo, in pixels of the cached thumbnail the
/// detector ran against.
/// </summary>
public readonly record struct FaceBounds(int X, int Y, int Width, int Height)
{
    public int Area => Width * Height;

    /// <summary>
    /// Where this face lands when the picture around it is turned clockwise.
    /// </summary>
    /// <param name="width">The picture's width before the turn.</param>
    /// <param name="height">The picture's height before the turn.</param>
    /// <param name="degrees">A quarter turn: 0, 90, 180 or 270.</param>
    /// <remarks>
    /// Turning a photograph the right way up must not cost the names on it. The
    /// alternative is to detect its faces again, and detecting replaces what a
    /// photo had - so every confirmation on that picture would be thrown away by
    /// the act of straightening it. Moving the boxes keeps all of it: the same
    /// pixels are the same face, they are simply somewhere else now.
    ///
    /// <para>The vector recorded for each face is not touched and does not need
    /// to be. It was computed from a crop aligned on the eyes and mouth, which
    /// is upright whichever way the picture it came from was stored.</para>
    /// </remarks>
    public FaceBounds TurnedClockwise(int width, int height, int degrees)
    {
        // Anticlockwise and repeated turns both arrive here, so the angle is
        // brought into 0-359 before anything is decided from it.
        int turn = (((degrees % 360) + 360) % 360);

        return turn switch
        {
            90 => new FaceBounds(height - Y - Height, X, Height, Width),
            180 => new FaceBounds(width - X - Width, height - Y - Height, Width, Height),
            270 => new FaceBounds(Y, width - X - Width, Height, Width),
            _ => this,
        };
    }
}
