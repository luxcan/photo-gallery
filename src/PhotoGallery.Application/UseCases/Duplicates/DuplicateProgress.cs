namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>How far a look for duplicates has got, for the overlay.</summary>
/// <param name="Phase">The named step it is in, said on the overlay.</param>
/// <param name="Done">Photographs measured so far; zero while finding the sets.</param>
/// <param name="Total">Photographs to measure; zero while finding the sets.</param>
public sealed record DuplicateProgress(string Phase, int Done, int Total)
{
    /// <summary>Grouping the library by its hashes. Quick, and has no count.</summary>
    public const string Finding = "Finding the copies";

    /// <summary>Reading the previews of the near copies, which is the slow part.</summary>
    public const string Measuring = "Measuring the copies that look the same";

    public double Fraction => Total == 0 ? 0d : (double)Done / Total;
}
