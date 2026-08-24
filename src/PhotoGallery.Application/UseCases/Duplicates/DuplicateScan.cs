namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>What a look through the library for duplicates turned up.</summary>
public sealed record DuplicateScan(
    int Weighed,
    int ExactSets,
    int ExactRedundant,
    long ExactBytes,
    int NearSets,
    int NearRedundant,
    long NearBytes)
{
    public long TotalBytes => ExactBytes + NearBytes;

    public string Summary => ExactSets + NearSets == 0
        ? $"Looked at {Weighed:N0} photos and found no duplicates."
        : $"{ExactSets:N0} identical and {NearSets:N0} near-identical sets, "
          + $"holding {ExactRedundant + NearRedundant:N0} redundant copies "
          + $"({Gigabytes(TotalBytes)}).";

    public static string Gigabytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024 / 1024:F2} GB"
        : $"{bytes / 1024d / 1024:F0} MB";
}
