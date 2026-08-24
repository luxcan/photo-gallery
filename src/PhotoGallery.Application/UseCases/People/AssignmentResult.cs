namespace PhotoGallery.Application.UseCases.People;

/// <summary>What saying who someone is changed.</summary>
/// <param name="Proposed">
/// How many more faces the app now believes are this person, offered for review
/// rather than treated as settled.
/// </param>
/// <param name="Considered">
/// How many unnamed faces were weighed to arrive at those proposals, so a round
/// that offers nothing can say whether it looked.
/// </param>
/// <param name="Matched">
/// How many faces cleared the threshold altogether, which can be more than
/// <paramref name="Proposed"/>. Reported rather than hidden: three people all
/// showing exactly three hundred is a cap being met, and a number that stops at
/// a round figure without saying so reads as the whole answer.
/// </param>
public sealed record AssignmentResult(
    int PersonId,
    string DisplayName,
    int Assigned,
    int Eras,
    int Proposed,
    int Matched,
    int Considered)
{
    public bool WasCapped => Matched > Proposed;

    public string Summary => Proposed == 0
        ? Settled
        : WasCapped
            ? $"{Settled}. {Matched:N0} more look like them - "
              + $"showing the closest {Proposed:N0}, and the rest after you answer these."
            : $"{Settled}, and {Waiting}";

    /// <summary>What was just decided, counted as a sentence rather than a sum.</summary>
    /// <remarks>
    /// Answering the last question about somebody is the most likely of all of
    /// these to be read, and it said "1 faces are Vera".
    /// </remarks>
    private string Settled => Assigned == 1
        ? $"1 face is {DisplayName}"
        : $"{Assigned:N0} faces are {DisplayName}";

    /// <summary>What is left to answer, counted the same way.</summary>
    private string Waiting => Proposed == 1
        ? "1 more looks like them"
        : $"{Proposed:N0} more look like them";
}
