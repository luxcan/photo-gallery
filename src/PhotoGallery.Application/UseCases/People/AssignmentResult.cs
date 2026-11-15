namespace PhotoGallery.Application.UseCases.People;

/// <summary>What saying who someone is changed.</summary>
/// <remarks>
/// Only what was decided. What the answer means for the rest of the library -
/// how many more faces look like them - is worked out by a suggestion round that
/// runs after the answer is saved, so it is not known when this is written, and
/// a sentence that guessed at it would be wrong as often as not.
/// </remarks>
/// <param name="WereRejected">
/// True when the answer was "no, they are not", so the sentence says that
/// rather than the opposite of what was just decided.
/// </param>
public sealed record AssignmentResult(
    int PersonId,
    string DisplayName,
    int Assigned,
    int Eras,
    bool WereRejected = false)
{
    /// <summary>What was just decided, counted as a sentence rather than a sum.</summary>
    /// <remarks>
    /// Answering the last question about somebody is the most likely of all of
    /// these to be read, and it said "1 faces are Vera".
    /// </remarks>
    public string Summary => (Assigned == 1, WereRejected) switch
    {
        (true, false) => $"1 face is {DisplayName}",
        (true, true) => $"1 face is not {DisplayName}",
        (false, false) => $"{Assigned:N0} faces are {DisplayName}",
        (false, true) => $"{Assigned:N0} faces are not {DisplayName}",
    };
}
