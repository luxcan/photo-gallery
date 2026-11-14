using PhotoGallery.Application.UseCases;

namespace PhotoGallery.Tests.Application;

public sealed class ProgressPacerTests
{
    [Fact]
    public void IsDue_OnceATickHasPassed()
    {
        var pacer = new ProgressPacer(new SteppingClock(ProgressPacer.Interval));

        Assert.True(pacer.IsDue());
        Assert.True(pacer.IsDue());
    }

    [Fact]
    public void IsNotDue_InsideATick()
    {
        var pacer = new ProgressPacer(new SteppingClock(TimeSpan.FromMilliseconds(40)));

        // 40 ms, then 80 ms, then 120 ms since the pacer was made.
        Assert.False(pacer.IsDue());
        Assert.False(pacer.IsDue());
        Assert.True(pacer.IsDue());

        // The tick starts again from the report just let through.
        Assert.False(pacer.IsDue());
    }

    [Fact]
    public void Regardless_ReportsNowAndRestartsTheTick()
    {
        var pacer = new ProgressPacer(new SteppingClock(TimeSpan.FromMilliseconds(60)));

        Assert.True(pacer.IsDue(regardless: true));
        Assert.False(pacer.IsDue());
        Assert.True(pacer.IsDue());
    }

    [Fact]
    public void OnlyOneOfManyThreadsWinsATick()
    {
        // The clock stands still after the first tick, so exactly one report
        // is due however many workers ask for it at once.
        var clock = new SettableClock();
        var pacer = new ProgressPacer(clock);
        clock.Now = ProgressPacer.Interval.Ticks;

        int won = 0;
        Parallel.For(0, 1_000, _ =>
        {
            if (pacer.IsDue())
            {
                Interlocked.Increment(ref won);
            }
        });

        Assert.Equal(1, won);
    }

    private sealed class SettableClock : TimeProvider
    {
        public long Now { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Now;
    }
}
