using CommunityToolkit.Mvvm.ComponentModel;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.UseCases.People;

namespace PhotoGallery.App.People;

/// <summary>
/// Works out who every unnamed face looks like, one round at a time, in the
/// background - and the small bar in the corner that says so.
/// </summary>
/// <remarks>
/// A round weighs every face in the library against everybody named. It used to
/// run inside every answer - 2.3 to 2.6 s an answer when this was asked for,
/// 1.2 s on the copy of the real library it was re-measured on, against 0.2 to
/// 0.35 s for the save alone - so naming ten faces in a row was ten waits. Now
/// an answer is saved on its own and asks for a round here.
///
/// <para>Coalesced, because a round is a sweep over everybody: one that starts
/// after an answer sees that answer and every one before it. So asking while a
/// round is running only marks one more as needed, and however many answers
/// arrive meanwhile, exactly one more runs when the current one ends - ten
/// quick answers are at most two rounds, not ten.</para>
///
/// <para>One at a time, app-wide, including the rounds the long passes run
/// inline (<see cref="RunExclusiveAsync{T}"/>): SQLite has one writer, and two
/// rounds withdrawing and remaking the same proposals would each undo the
/// other.</para>
/// </remarks>
public sealed partial class SuggestionRounds : ObservableObject
{
    /// <summary>What the bar says while a round runs.</summary>
    public const string RunningCaption = "Updating suggestions";

    private readonly Func<Task<ProposalRound>> _round;
    private readonly TimeSpan _failureShownFor;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly object _sync = new();

    private bool _running;
    private bool _oneMoreNeeded;
    private int _started;
    private long _stretch;
    private Task _loop = Task.CompletedTask;

    /// <summary>Whether the bar is up: a round is running, or one has just failed.</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>Whether the bar's line is a failure rather than work in progress.</summary>
    [ObservableProperty]
    private bool _hasFailed;

    [ObservableProperty]
    private string _caption = string.Empty;

    /// <summary>
    /// Which round of those known so far, as "1 of 2", or empty while there is
    /// only the one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCount))]
    private string _count = string.Empty;

    /// <param name="round">
    /// Runs one round and returns what it offered. The caller decides where -
    /// off the UI thread and in a scope of its own.
    /// </param>
    /// <param name="failureShownFor">
    /// How long a failure stays in the bar. Long enough to be read; it is in the
    /// log as well.
    /// </param>
    public SuggestionRounds(Func<Task<ProposalRound>> round, TimeSpan? failureShownFor = null)
    {
        ArgumentNullException.ThrowIfNull(round);

        _round = round;
        _failureShownFor = failureShownFor ?? TimeSpan.FromSeconds(8);
    }

    /// <summary>Raised after each round that finished, with what it offered.</summary>
    public event EventHandler<ProposalRound>? RoundFinished;

    public bool HasCount => Count.Length > 0;

    /// <summary>
    /// The rounds asked for so far, finished once none is running or waiting.
    /// </summary>
    /// <remarks>Internal so a test can wait for the background work to settle.</remarks>
    internal Task Settled
    {
        get
        {
            lock (_sync)
            {
                return _loop;
            }
        }
    }

    /// <summary>
    /// Asks for the suggestions to be worked out again, and returns at once.
    /// </summary>
    public void Request()
    {
        lock (_sync)
        {
            if (_running)
            {
                _oneMoreNeeded = true;
                ShowProgress();
                return;
            }

            _running = true;
            _started = 0;
            _stretch++;
            _loop = RunAsync();
        }
    }

    /// <summary>
    /// Runs work that includes a round of its own, never alongside one of these.
    /// </summary>
    /// <remarks>
    /// For the passes that already hold the window under the long-pass overlay
    /// and sweep the library as part of what they do. They wait for a round
    /// already running to finish rather than racing it.
    /// </remarks>
    public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _oneAtATime.WaitAsync().ConfigureAwait(true);
        try
        {
            return await work().ConfigureAwait(true);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// Runs one round now and waits for it, without the bar in the corner.
    /// </summary>
    /// <remarks>
    /// For answers given on People, where the window is already covered by the
    /// panel saying the answers are being saved: the user asked for that one
    /// panel to stay up until the suggestions are done too, rather than a panel
    /// and then a bar. Still one round at a time - it waits for one already
    /// running - and still tells everything showing suggestions when it is over.
    /// </remarks>
    public Task<ProposalRound> RunNowAsync() =>
        RunExclusiveAsync(async () =>
        {
            ProposalRound round = await _round().ConfigureAwait(true);
            RoundFinished?.Invoke(this, round);
            return round;
        });

    private async Task RunAsync()
    {
        // Out of the caller's frame before anything else, so Request returns
        // straight away whatever the round does before its first await.
        await Task.Yield();

        while (true)
        {
            lock (_sync)
            {
                _oneMoreNeeded = false;
                _started++;
                ShowProgress();
            }

            Exception? failure = null;

            await _oneAtATime.WaitAsync().ConfigureAwait(true);
            try
            {
                ProposalRound round = await _round().ConfigureAwait(true);
                RoundFinished?.Invoke(this, round);
            }
            catch (Exception ex)
            {
                // Every exception, not only the expected library failures. This
                // runs with nobody awaiting it, so anything let through would be
                // lost unobserved and leave the runner believing a round was
                // still going - and no answer after it would ever be followed up.
                failure = ex;
                DiagnosticLog.Write("could not update the suggestions", ex);
            }
            finally
            {
                _oneAtATime.Release();
            }

            lock (_sync)
            {
                if (_oneMoreNeeded)
                {
                    continue;
                }

                _running = false;

                if (failure is null)
                {
                    Hide();
                }
                else
                {
                    ShowFailure(failure);
                    _ = HideFailureLaterAsync(_stretch);
                }

                return;
            }
        }
    }

    /// <summary>Says which round this is of the ones known so far.</summary>
    private void ShowProgress()
    {
        int known = _started + (_oneMoreNeeded ? 1 : 0);

        HasFailed = false;
        Caption = RunningCaption;
        Count = known > 1 ? $"{Math.Max(_started, 1):N0} of {known:N0}" : string.Empty;
        IsVisible = true;
    }

    private void ShowFailure(Exception failure)
    {
        HasFailed = true;
        // Plain words in the corner; the details are in the diagnostic log,
        // written where the round failed. The next answer asks again.
        Caption = "Suggestions could not be updated - they will be tried again after your next answer";
        Count = string.Empty;
        IsVisible = true;
    }

    private void Hide()
    {
        IsVisible = false;
        HasFailed = false;
        Caption = string.Empty;
        Count = string.Empty;
    }

    /// <summary>
    /// Takes a failure down after a while, unless another round has started
    /// since and the bar is saying something else.
    /// </summary>
    private async Task HideFailureLaterAsync(long stretch)
    {
        await Task.Delay(_failureShownFor).ConfigureAwait(true);

        lock (_sync)
        {
            if (!_running && _stretch == stretch)
            {
                Hide();
            }
        }
    }
}
