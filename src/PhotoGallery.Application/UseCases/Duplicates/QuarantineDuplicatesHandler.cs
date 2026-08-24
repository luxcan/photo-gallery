using PhotoGallery.Application.Ports;

namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>
/// Sets aside the redundant copies in a duplicate set.
/// </summary>
/// <remarks>
/// Moves, never deletes. The file goes into the working folder's quarantine
/// under the same relative path it had in the library, which is what makes
/// putting it back mechanical.
///
/// <para>The keeper is never touched, and a set is only marked resolved when
/// every redundant copy in it actually moved. A set half-done stays on the
/// screen saying so, because the alternative is a set that looks finished while
/// a copy nobody can find is still on the share.</para>
/// </remarks>
public sealed class QuarantineDuplicatesHandler
{
    private readonly IDuplicateRepository _duplicates;
    private readonly IQuarantineStore _quarantine;

    public QuarantineDuplicatesHandler(
        IDuplicateRepository duplicates, IQuarantineStore quarantine)
    {
        _duplicates = duplicates;
        _quarantine = quarantine;
    }

    public async Task<QuarantineResult> HandleAsync(
        IReadOnlyList<int> setIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setIds);

        int moved = 0, refused = 0, sets = 0;
        long bytes = 0;
        var manifest = new List<QuarantinedCopy>();

        foreach (int setId in setIds)
        {
            // Stopped rather than thrown out of. Files have already moved and
            // rows have already been marked by the time a cancel arrives, and an
            // exception here would take the tally of what moved - and the
            // manifest naming it - out with it.
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (await _duplicates.FindAsync(setId, cancellationToken).ConfigureAwait(false)
                is not DuplicateSetView set)
            {
                continue;
            }

            var wentAway = new List<int>();
            DateTime when = DateTime.UtcNow;

            foreach (DuplicateCopy copy in set.Redundant)
            {
                // The hash goes with it: this file was proved a duplicate by its
                // digest, so the copy can be proved intact by the same one
                // before the library's original is deleted.
                bool gone = await _quarantine
                    .PutAsync(
                        copy.FullPath,
                        copy.PhotoSourceId,
                        copy.RelativePath,
                        copy.ContentHash,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!gone)
                {
                    refused++;
                    continue;
                }

                wentAway.Add(copy.AssetId);
                moved++;
                bytes += copy.Length;
                manifest.Add(new QuarantinedCopy(
                    copy.AssetId, copy.PhotoSourceId, copy.RelativePath,
                    copy.FullPath, copy.Length, when));
            }

            if (wentAway.Count > 0)
            {
                await _duplicates
                    .SetQuarantinedAsync(wentAway, when, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Only when every copy in it went. A set still holding a file that
            // would not move is a set with something left to do.
            if (wentAway.Count == set.Redundant.Count)
            {
                await _duplicates.MarkResolvedAsync(setId, true, cancellationToken)
                    .ConfigureAwait(false);
                sets++;
            }
        }

        if (manifest.Count > 0)
        {
            // Not cancellable: these files have already moved, and the record of
            // where they came from is worth least at exactly the moment it is
            // most likely to be wanted.
            await _quarantine.WriteManifestAsync(manifest, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return new QuarantineResult(sets, moved, refused, bytes);
    }
}
