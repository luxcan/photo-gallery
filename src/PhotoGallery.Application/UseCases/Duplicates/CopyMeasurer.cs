using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Assets;
using PhotoGallery.Domain.Faces;

namespace PhotoGallery.Application.UseCases.Duplicates;

/// <summary>
/// Measures the copies in "Looks the same" sets for the keeper ladder: eyes and
/// sharpness per face, sharpness and exposure per preview (PRP 14).
/// </summary>
/// <remarks>
/// Cached previews only, so it costs no trip to the photo sources and runs with
/// them switched off. Each photograph is kept as soon as it is measured, so a
/// Stop loses nothing and the next run carries on with what is left; a
/// photograph already measured is not read again unless its preview or its
/// faces have changed since.
///
/// <para>One photograph at a time, so the eye graphs see one inference at a
/// time - a correctness limit on the graphics card, not a tuning choice.</para>
/// </remarks>
public sealed class CopyMeasurer
{
    private readonly IPhotoQualityRepository _quality;
    private readonly IPreviewQualityReader _previews;
    private readonly IThumbnailStore _thumbnails;
    private readonly IEyeMeasurer _eyes;

    public CopyMeasurer(
        IPhotoQualityRepository quality,
        IPreviewQualityReader previews,
        IThumbnailStore thumbnails,
        IEyeMeasurer eyes)
    {
        _quality = quality;
        _previews = previews;
        _thumbnails = thumbnails;
        _eyes = eyes;
    }

    /// <summary>Measures whichever of these photographs need it.</summary>
    /// <param name="assetIds">The near-set copies. Identical copies are never passed.</param>
    /// <param name="readEyes">Whether the eye model is installed; without it faces get sharpness only.</param>
    /// <param name="progress">Told after each photograph.</param>
    /// <param name="cancellationToken">Stops between photographs; what is done is kept.</param>
    /// <returns>How many photographs were measured.</returns>
    public async Task<int> MeasureAsync(
        IReadOnlyCollection<int> assetIds,
        bool readEyes,
        IProgress<DuplicateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        IReadOnlyList<MeasuringCandidate> candidates =
            await _quality.GetCandidatesAsync(assetIds, cancellationToken).ConfigureAwait(false);

        List<(MeasuringCandidate Candidate, DateTime Written)> due = [];
        foreach (MeasuringCandidate candidate in candidates)
        {
            if (RenditionName.IsSafeFileName(candidate.ThumbnailName)
                && _thumbnails.PreviewWrittenUtc(candidate.ThumbnailName) is DateTime written
                && candidate.IsDue(written, readEyes))
            {
                due.Add((candidate, written));
            }
        }

        // Not named on the overlay when there is nothing to do: the step would
        // flash up over the grouping and say nothing.
        if (due.Count == 0)
        {
            return 0;
        }

        int measured = 0;
        progress?.Report(new DuplicateProgress(DuplicateProgress.Measuring, 0, due.Count));

        for (int at = 0; at < due.Count; at++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (MeasuringCandidate candidate, DateTime written) = due[at];
            if (await MeasureOneAsync(candidate, written, readEyes, cancellationToken).ConfigureAwait(false)
                is PhotoMeasurement measurement)
            {
                // Not cancellable: the preview has been read and the graphs run,
                // and a Stop arriving now should keep that rather than throw it away.
                await _quality.SaveAsync(measurement, DateTime.UtcNow, CancellationToken.None).ConfigureAwait(false);
                measured++;
            }

            progress?.Report(new DuplicateProgress(DuplicateProgress.Measuring, at + 1, due.Count));
        }

        return measured;
    }

    /// <summary>
    /// One photograph's measurement, or null when its preview will not open -
    /// it is then left unmeasured and simply has no readings on the ladder.
    /// </summary>
    private async Task<PhotoMeasurement?> MeasureOneAsync(
        MeasuringCandidate candidate, DateTime written, bool readEyes, CancellationToken cancellationToken)
    {
        string path = _thumbnails.ResolvePreviewPath(candidate.ThumbnailName);
        FaceBounds[] boxes = [.. candidate.Faces.Select(face => face.Bounds)];

        if (_previews.Read(path, boxes) is not PreviewReading reading)
        {
            return null;
        }

        IReadOnlyList<EyeMeasurement>? eyes = readEyes && boxes.Length > 0
            ? await _eyes.MeasureAsync(path, boxes, cancellationToken).ConfigureAwait(false)
            : null;

        var faces = new List<FaceMeasurement>(candidate.Faces.Count);
        for (int i = 0; i < candidate.Faces.Count; i++)
        {
            EyeMeasurement? eye = eyes is not null && i < eyes.Count ? eyes[i] : null;
            faces.Add(new FaceMeasurement(
                candidate.Faces[i].FaceId,
                eye is EyeMeasurement read ? EyeVerdict.Measured.Judge(read) : null,
                eye is EyeMeasurement wider ? Wider(wider) : null,
                reading.FaceSharpness[i]));
        }

        return new PhotoMeasurement(candidate.AssetId, candidate.ThumbnailName, written, reading, faces);
    }

    /// <summary>
    /// The more open eye, which is the one Closed has to be below as well - so
    /// this one number says how near a face came to reading Closed.
    /// </summary>
    private static double? Wider(EyeMeasurement eyes) =>
        (eyes.LeftOpenness, eyes.RightOpenness) switch
        {
            (double left, double right) => Math.Max(left, right),
            (double left, null) => left,
            (null, double right) => right,
            _ => null,
        };
}
