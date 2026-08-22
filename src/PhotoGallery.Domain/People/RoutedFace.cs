namespace PhotoGallery.Domain.People;

/// <summary>Who a face was given to, and how clear-cut that was.</summary>
/// <param name="Runner">
/// The next best person's score, or negative infinity when there was nobody
/// else. Carried so a caller can say how close the call was rather than only
/// who won.
/// </param>
public readonly record struct RoutedFace(int PersonId, float Score, float Runner)
{
    /// <summary>How far ahead of the next person this was.</summary>
    public float Lead => float.IsNegativeInfinity(Runner) ? Score : Score - Runner;
}
