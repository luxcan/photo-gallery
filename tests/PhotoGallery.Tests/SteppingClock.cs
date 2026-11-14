namespace PhotoGallery.Tests;

/// <summary>A clock that moves on by a fixed step every time it is read.</summary>
/// <remarks>
/// Lets a test say how long each item of a pass "took" without waiting for it:
/// a pass that reads the clock once per item sees exactly <c>step</c> go by.
/// </remarks>
internal sealed class SteppingClock(TimeSpan step) : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Add(ref _ticks, step.Ticks);
}
