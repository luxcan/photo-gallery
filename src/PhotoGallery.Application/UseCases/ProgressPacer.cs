namespace PhotoGallery.Application.UseCases;

/// <summary>
/// Decides when a running count goes to the screen: ten times a second.
/// </summary>
/// <remarks>
/// By time rather than by item count, so a count moves as steadily when each
/// item takes a second as when it takes a millisecond. Counting every Nth item
/// left a slow pass - a video is about a second over the share - standing
/// still for ten seconds at a time, which reads as a hang; reporting every item
/// of a fast one would be thousands of redraws a second for nothing the eye
/// can take in.
///
/// <para>Safe to ask from many threads at once, as the parallel passes do:
/// one caller wins each tick and the rest are told to wait.</para>
/// </remarks>
public sealed class ProgressPacer
{
    /// <summary>The least time between two reports the pacer lets through.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private readonly TimeProvider _clock;
    private long _reportedAt;

    public ProgressPacer(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _reportedAt = _clock.GetTimestamp();
    }

    /// <summary>
    /// True when a report is due, and from then the next one is a tick away.
    /// </summary>
    /// <param name="regardless">
    /// Report now whatever the clock says - for news that would otherwise be
    /// lost, such as a crawl reaching a folder it will be through within a tick.
    /// </param>
    /// <remarks>Reads the clock once per call.</remarks>
    public bool IsDue(bool regardless = false)
    {
        long now = _clock.GetTimestamp();
        long reportedAt = Volatile.Read(ref _reportedAt);

        if (regardless)
        {
            Volatile.Write(ref _reportedAt, now);
            return true;
        }

        return _clock.GetElapsedTime(reportedAt, now) >= Interval
            && Interlocked.CompareExchange(ref _reportedAt, now, reportedAt) == reportedAt;
    }
}
