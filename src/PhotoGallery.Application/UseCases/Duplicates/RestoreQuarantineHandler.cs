using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>
/// Puts set-aside copies back where they came from.
/// </summary>
/// <remarks>
/// It needs no manifest and no memory of the decision: the quarantine mirrors
/// the library's own layout, so a copy's way home is its photo source and its
/// relative path - the two things its row has said all along.
///
/// <para>The row is only brought back once the file is actually there. A row
/// saying a picture is in the library when it is not is the one outcome worse
/// than leaving it quarantined.</para>
///
/// <para><b>No button calls this.</b> The user asked for the restore control to
/// be taken off the Duplicates screen, so nothing in the app resolves it today.
/// It is kept, and kept tested, because the same layout that makes it work makes
/// restoring by hand work too - dragging a folder out of the quarantine back
/// into the library is exactly what this does - and because putting the control
/// back is one line of wiring rather than a feature.</para>
/// </remarks>
public sealed class RestoreQuarantineHandler
{
    private readonly IDuplicateRepository _duplicates;
    private readonly IQuarantineStore _quarantine;

    public RestoreQuarantineHandler(
        IDuplicateRepository duplicates, IQuarantineStore quarantine)
    {
        _duplicates = duplicates;
        _quarantine = quarantine;
    }

    /// <param name="assetIds">
    /// Which copies to bring back, or null for everything currently set aside.
    /// </param>
    public async Task<RestoreResult> HandleAsync(
        IReadOnlyList<int>? assetIds = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QuarantinedCopy> waiting =
            await _duplicates.GetQuarantinedAsync(cancellationToken).ConfigureAwait(false);

        if (assetIds is not null)
        {
            HashSet<int> wanted = [.. assetIds];
            waiting = [.. waiting.Where(copy => wanted.Contains(copy.AssetId))];
        }

        var back = new List<int>();
        int refused = 0;

        foreach (QuarantinedCopy copy in waiting)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool home = await _quarantine
                .TakeBackAsync(
                    copy.OriginalFullPath, copy.PhotoSourceId, copy.RelativePath, cancellationToken)
                .ConfigureAwait(false);

            if (home)
            {
                back.Add(copy.AssetId);
            }
            else
            {
                refused++;
            }
        }

        if (back.Count > 0)
        {
            await _duplicates
                .SetQuarantinedAsync(back, null, cancellationToken)
                .ConfigureAwait(false);
        }

        return new RestoreResult(back.Count, refused);
    }
}
