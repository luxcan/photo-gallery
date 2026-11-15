using PhotoGallery.App.People;
using PhotoGallery.Application.UseCases.People;

namespace PhotoGallery.Tests.App;

/// <summary>
/// The background suggestion rounds: one at a time, coalesced, and said in the
/// corner while they run.
/// </summary>
/// <remarks>
/// A round is a sweep over the whole library, and it used to run inside every
/// answer - so naming ten faces in a row queued ten. These pin the rules that replaced it: an answer asks and
/// returns, a round asked for while one runs only marks one more as needed, and
/// two never run at once.
///
/// <para>Every round here is held by the test until it says so, so "while a
/// round is running" is a state the test is in rather than a race it hopes to
/// win.</para>
/// </remarks>
public sealed class SuggestionRoundsTests
{
    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ARequestWhileIdle_RunsOneRound()
    {
        var rounds = new HeldRounds();
        var runner = new SuggestionRounds(rounds.RunAsync);
        int finished = 0;
        runner.RoundFinished += (_, _) => finished++;

        runner.Request();
        await rounds.Started(1);

        Assert.True(runner.IsVisible);
        Assert.Equal(SuggestionRounds.RunningCaption, runner.Caption);
        Assert.False(runner.HasCount, "a lone round counted itself as one of one");

        rounds.FinishNext();
        await runner.Settled.WaitAsync(s_bound);

        Assert.Equal(1, rounds.Count);
        Assert.Equal(1, finished);
        Assert.False(runner.IsVisible);
    }

    [Fact]
    public async Task TenRequestsDuringARound_RunExactlyOneMore()
    {
        var rounds = new HeldRounds();
        var runner = new SuggestionRounds(rounds.RunAsync);

        runner.Request();
        await rounds.Started(1);

        for (int i = 0; i < 10; i++)
        {
            runner.Request();
        }

        // The bar says there is one more behind this one, and no more than one.
        Assert.Equal("1 of 2", runner.Count);
        Assert.True(runner.HasCount);

        rounds.FinishNext();
        await rounds.Started(2);
        Assert.Equal("2 of 2", runner.Count);

        rounds.FinishNext();
        await runner.Settled.WaitAsync(s_bound);

        Assert.Equal(2, rounds.Count);
        Assert.False(runner.IsVisible);
        Assert.Equal(string.Empty, runner.Count);
    }

    [Fact]
    public async Task ARequestDuringTheSecondRound_CountsAThird()
    {
        var rounds = new HeldRounds();
        var runner = new SuggestionRounds(rounds.RunAsync);

        runner.Request();
        await rounds.Started(1);
        runner.Request();
        rounds.FinishNext();
        await rounds.Started(2);

        runner.Request();

        Assert.Equal("2 of 3", runner.Count);

        rounds.FinishNext();
        await rounds.Started(3);
        rounds.FinishNext();
        await runner.Settled.WaitAsync(s_bound);

        Assert.Equal(3, rounds.Count);
    }

    [Fact]
    public async Task TwoRoundsNeverRunAtOnce()
    {
        // Including the ones the long passes run inline under their overlay:
        // SQLite has one writer, and two rounds withdrawing and remaking the same
        // proposals would each undo the other.
        var rounds = new HeldRounds();
        var runner = new SuggestionRounds(rounds.RunAsync);

        runner.Request();
        await rounds.Started(1);

        bool inlineRan = false;
        Task inline = runner.RunExclusiveAsync(async () =>
        {
            rounds.Enter();
            inlineRan = true;
            await Task.Yield();
            rounds.Leave();
            return 0;
        });

        for (int i = 0; i < 5; i++)
        {
            runner.Request();
        }

        await Task.Delay(50);
        Assert.False(inlineRan, "the inline round started while a background one was running");

        rounds.FinishNext();
        await inline.WaitAsync(s_bound);
        await rounds.Started(2);
        rounds.FinishNext();
        await runner.Settled.WaitAsync(s_bound);

        Assert.True(inlineRan);
        Assert.Equal(1, rounds.MostAtOnce);
    }

    [Fact]
    public async Task AFailedRound_IsSaidInTheBar_AndTheNextRequestStillWorks()
    {
        var rounds = new HeldRounds();
        var runner = new SuggestionRounds(rounds.RunAsync, failureShownFor: TimeSpan.FromMilliseconds(50));

        runner.Request();
        await rounds.Started(1);
        rounds.FailNext(new IOException("the index is busy"));
        await runner.Settled.WaitAsync(s_bound);

        Assert.True(runner.IsVisible, "a failure went unsaid");
        Assert.True(runner.HasFailed);
        Assert.StartsWith("Suggestions could not be updated", runner.Caption, StringComparison.Ordinal);
        Assert.DoesNotContain("the index is busy", runner.Caption, StringComparison.Ordinal);

        // Taken down after a while rather than left in the corner for good.
        await TestWait.ForAsync(() => !runner.IsVisible, "the failure to be taken down");

        runner.Request();
        await rounds.Started(2);
        Assert.False(runner.HasFailed);
        rounds.FinishNext();
        await runner.Settled.WaitAsync(s_bound);

        Assert.Equal(2, rounds.Count);
        Assert.False(runner.IsVisible);
    }

    [Fact]
    public async Task AFailureFollowedByARoundThatWorks_LeavesNothingInTheBar()
    {
        var rounds = new HeldRounds();
        var runner = new SuggestionRounds(rounds.RunAsync);

        runner.Request();
        await rounds.Started(1);
        runner.Request();
        rounds.FailNext(new IOException("the index is busy"));
        await rounds.Started(2);
        rounds.FinishNext();
        await runner.Settled.WaitAsync(s_bound);

        Assert.False(runner.IsVisible);
        Assert.False(runner.HasFailed);
    }

    /// <summary>
    /// Rounds that run only when the test lets each one go, counting how many
    /// started and how many were ever running together.
    /// </summary>
    private sealed class HeldRounds
    {
        private readonly object _sync = new();
        private readonly Queue<TaskCompletionSource<ProposalRound>> _running = new();
        private int _inFlight;

        public int Count { get; private set; }

        public int MostAtOnce { get; private set; }

        public Task<ProposalRound> RunAsync()
        {
            var round = new TaskCompletionSource<ProposalRound>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_sync)
            {
                Count++;
                _running.Enqueue(round);
                Enter();
            }

            return Finish(round.Task);
        }

        public void Enter()
        {
            lock (_sync)
            {
                _inFlight++;
                MostAtOnce = Math.Max(MostAtOnce, _inFlight);
            }
        }

        public void Leave()
        {
            lock (_sync)
            {
                _inFlight--;
            }
        }

        /// <summary>Waits until this many rounds have started.</summary>
        public Task Started(int count) =>
            TestWait.ForAsync(
                () =>
                {
                    lock (_sync)
                    {
                        return Count >= count && _running.Count > 0;
                    }
                },
                $"round {count} to start");

        public void FinishNext() =>
            Next().SetResult(new ProposalRound(0, 0, 0, new Dictionary<int, PersonProposals>()));

        public void FailNext(Exception failure) => Next().SetException(failure);

        private TaskCompletionSource<ProposalRound> Next()
        {
            lock (_sync)
            {
                return _running.Dequeue();
            }
        }

        private async Task<ProposalRound> Finish(Task<ProposalRound> round)
        {
            try
            {
                return await round.ConfigureAwait(false);
            }
            finally
            {
                Leave();
            }
        }
    }
}
