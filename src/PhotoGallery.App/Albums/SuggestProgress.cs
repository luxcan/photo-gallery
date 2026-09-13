namespace PhotoGallery.App.Albums;

/// <summary>
/// What a look for photographs that fit an album is doing, while it does it.
/// </summary>
/// <remarks>
/// Two phases and they are not alike. Finding which photographs match is one
/// statement the database answers as a whole - there is no file being checked at
/// any moment, and a line claiming to name one would be invented. Getting them
/// ready afterwards is a picture at a time, and that one can honestly say which.
///
/// <para>So <see cref="Total"/> of zero means a phase with no countable parts
/// rather than a phase with nothing in it, and the overlay shows the moving bar
/// it already shows for the passes that cannot count either.</para>
/// </remarks>
/// <param name="What">
/// The phase, as a sentence to put on screen.
/// </param>
/// <param name="Target">
/// What is being worked on at this moment - a file name while the pictures are
/// being read, and empty while the database is being asked.
/// </param>
public sealed record SuggestProgress(string What, string Target, int Done, int Total)
{
    /// <summary>The first phase, which can only say that it is happening.</summary>
    public static SuggestProgress Searching { get; } =
        new("Looking for photographs that fit", string.Empty, 0, 0);

    /// <summary>Whether there is a count worth drawing a bar from.</summary>
    public bool IsCountable => Total > 0;
}
