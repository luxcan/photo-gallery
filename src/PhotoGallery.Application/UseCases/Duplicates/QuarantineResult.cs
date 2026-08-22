namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>What setting copies aside actually did.</summary>
/// <param name="Refused">
/// Copies that would not move - open elsewhere, read-only, or on a share that
/// has gone away. Reported rather than swallowed: the space was not reclaimed
/// and the set is not finished.
/// </param>
public sealed record QuarantineResult(int Sets, int Moved, int Refused, long Bytes)
{
    public string Summary => Moved == 0
        ? Refused == 0
            ? "Nothing was set aside."
            : $"Nothing could be moved. {Refused:N0} files would not go."
        : Refused == 0
            ? $"{Moved:N0} copies set aside, {DuplicateScan.Gigabytes(Bytes)} reclaimed. "
              + "They are in the quarantine folder until you empty it."
            : $"{Moved:N0} copies set aside ({DuplicateScan.Gigabytes(Bytes)}). "
              + $"{Refused:N0} would not move and are still where they were.";
}

/// <summary>What putting copies back actually did.</summary>
public sealed record RestoreResult(int Restored, int Refused)
{
    public string Summary => Restored == 0
        ? Refused == 0
            ? "There was nothing to put back."
            : $"{Refused:N0} files could not be put back."
        : Refused == 0
            ? $"{Restored:N0} copies are back where they were."
            : $"{Restored:N0} copies are back. {Refused:N0} could not be put back.";
}
