using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Tests.Sharing;

/// <summary>
/// A library's sync folder, holding every question until a test lets it
/// answer - as a drive that sleeps does, or an address on a network that has
/// gone.
/// </summary>
/// <remarks>
/// Built the way the scan's own test of a folder that never answers is built:
/// a task nothing completes. What a screen needs and a scan does not is a folder
/// that comes back, so the gate can be opened, and closed again behind a
/// laptop that has left the house.
///
/// <para>Both ports that reach the folder wait on the one gate, so it does not
/// matter which of them the code under test asks: until the folder answers,
/// nothing comes back from either.</para>
///
/// <para>Whether the folder is there is answered here, rather than handed on to
/// the library's own exchange. A question a deadline has already given up on is
/// still waiting at the gate, and when the gate opens it must touch nothing - by
/// then the library's database is busy with whoever asked next. Everything else
/// is asked only by somebody still waiting for it, and is handed on.</para>
/// </remarks>
internal sealed class SlowSyncFolder : IDecisionExchange, IRenditionPool
{
    private readonly Library _library;
    private TaskCompletionSource _answering = NewGate();
    private TaskCompletionSource _asked = NewGate();

    /// <summary>A folder that answers nothing until <see cref="Answers"/>.</summary>
    public SlowSyncFolder(Library library) => _library = library;

    /// <summary>
    /// Finishes once somebody is waiting on the folder for an answer it has not
    /// given - which is the moment a screen is checking it.
    /// </summary>
    public Task Asked => _asked.Task;

    /// <summary>From now on the folder answers, including every question already waiting.</summary>
    public void Answers() => _answering.TrySetResult();

    /// <summary>From now on the folder answers nothing.</summary>
    public void GoesQuiet()
    {
        _answering = NewGate();
        _asked = NewGate();
    }

    public async Task<ExchangeReadiness> ReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return ExchangeReadiness.Ready;
    }

    public async Task PublishAsync(
        DecisionSet mine, CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        await _library.Exchange.PublishAsync(mine, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PublishedAnswers>> StandingAsync(
        CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return await _library.Exchange.StandingAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FetchedDecisions> FetchAsync(CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return await _library.Exchange.FetchAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(
        PreparedSet mine, CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        await _library.Pool.PublishAsync(mine, cancellationToken).ConfigureAwait(false);
    }

    async Task<IReadOnlyList<PreparedSet>> IRenditionPool.FetchAsync(
        CancellationToken cancellationToken)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return await _library.Pool.FetchAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PublishFacesAsync(
        FaceSet mine, CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return await _library.Pool.PublishFacesAsync(mine, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FaceSet>> FetchFacesAsync(
        CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return await _library.Pool.FetchFacesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<string>> NamesAsync(
        CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return await _library.Pool.NamesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> PushAsync(
        string thumbnailName,
        string tilePath,
        string previewPath,
        CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return await _library.Pool
            .PushAsync(thumbnailName, tilePath, previewPath, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> PullAsync(
        string thumbnailName,
        string tilePath,
        string previewPath,
        CancellationToken cancellationToken = default)
    {
        await AnsweredAsync().ConfigureAwait(false);
        return await _library.Pool
            .PullAsync(thumbnailName, tilePath, previewPath, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Holds a question until the folder answers, and says that one is held.
    /// </summary>
    /// <remarks>
    /// Uncancellable, as the question underneath is: the operating system gives
    /// up on an address when it likes, whatever the caller has decided since.
    /// </remarks>
    private Task AnsweredAsync()
    {
        Task answering = _answering.Task;

        if (!answering.IsCompleted)
        {
            _asked.TrySetResult();
        }

        return answering;
    }

    /// <summary>
    /// Continued off the thread that opens it, so a test opening the folder is
    /// not the thread that then runs everything that was waiting on it.
    /// </summary>
    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
