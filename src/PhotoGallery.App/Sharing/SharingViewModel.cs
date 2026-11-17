using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.App.Shell;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.App.Sharing;

/// <summary>
/// The Sharing screen: which folder the house shares through, who is up to
/// date, and one button that makes everybody's answers everybody's answers.
/// </summary>
/// <remarks>
/// <strong>One button, not two.</strong> Nobody wants to publish; they want the
/// names they typed last night to be on the other laptop. Separate "send" and
/// "receive" would be a procedure to remember, and the wrong order in it is not
/// an error anything could report - it quietly works and leaves the house one
/// merge behind.
///
/// <para>The screen opens saying what sharing is for, before a folder is
/// nominated: everybody's work kept level between the computers, through a
/// folder they all already reach.</para>
/// </remarks>
public sealed partial class SharingViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Where a share leaves a trace, because the screen's own account of it
    /// lives until the next press and no longer.
    /// </summary>
    /// <remarks>
    /// This feature is the one in the app whose failures are invisible on the
    /// machine that suffers them: a laptop nominating a folder nobody else can
    /// reach writes its file perfectly, reports a success, and is never heard
    /// from by anybody. The folder is the whole of what went wrong in that case
    /// and the whole of what is needed to see it, so it is written down beside
    /// the result rather than only drawn on a screen somebody has closed.
    /// </remarks>
    private readonly IActivityLog _log;

    /// <summary>
    /// One job at a time.
    /// </summary>
    /// <remarks>
    /// A read is raised by opening the screen, which can happen while a share is
    /// still running - and both of them touch the same folder.
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// The moment of the newest file behind the question, so that "not now" can
    /// mean this one rather than all of them for ever.
    /// </summary>
    /// <remarks>
    /// Kept for the life of the window and no longer. Somebody who says "not
    /// now" is answering about what is in the folder at that moment, and the
    /// next thing published is a new question - but a machine that publishes
    /// nightly should not be able to ask again about the same file every quarter
    /// of an hour until the app is closed.
    /// </remarks>
    private DateTime _declined = DateTime.MinValue;

    /// <summary>The folder this library shares answers through.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder))]
    [NotifyCanExecuteChangedFor(nameof(ShareCommand))]
    private string _folder = string.Empty;

    /// <summary>Why nothing can be exchanged, in the user's words.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    [NotifyCanExecuteChangedFor(nameof(ShareCommand))]
    private string _problem = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMachines))]
    private IReadOnlyList<MachineRow> _machines = [];

    /// <summary>
    /// Folders that might be the same folder, waiting on somebody to say.
    /// </summary>
    /// <remarks>
    /// Only ever filled by a share, because that is the only moment this
    /// library learns what another machine calls its folders. Cleared by the
    /// next one, so an offer somebody has answered does not sit there being
    /// answered again.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOffers))]
    private IReadOnlyList<PairingOffer> _offers = [];

    /// <summary>
    /// Computers this one has not synced with before, waiting on somebody to
    /// say whether to take their changes.
    /// </summary>
    /// <remarks>
    /// Read with the rest of the screen rather than only after a share, unlike
    /// the folder offers: the question is remembered until it is answered, and
    /// nothing from those computers arrives until it is.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewcomers))]
    private IReadOnlyList<MachineQuestion> _newcomers = [];

    /// <summary>What the last share did, or why it could not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    /// <summary>
    /// Answers waiting on a face this computer has not found, though the
    /// photograph itself is here.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasWaiting), nameof(HasWaitingOnFaces), nameof(WaitingOnFacesLabel))]
    private int _waitingOnFaces;

    /// <summary>
    /// Answers about a photograph that is here, waiting on something other than
    /// a face: the end of a scan, a small copy to turn, or an album to put it in.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasWaiting), nameof(HasWaitingHere), nameof(WaitingHereLabel))]
    private int _waitingHere;

    /// <summary>
    /// Answers waiting for photographs that are not in this library's folders
    /// under the names they were given.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasWaiting), nameof(HasWaitingOnPhotos), nameof(WaitingOnPhotosLabel))]
    private int _waitingOnPhotos;

    /// <summary>
    /// Answers about photographs in a folder this computer does not have.
    /// </summary>
    /// <remarks>
    /// The one group this screen must never hide, because it is the only one
    /// anybody can act on: an evening's work waiting for a folder nobody has
    /// added, and nothing else on the screen would ever hint at it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasWaiting), nameof(HasWaitingElsewhere), nameof(WaitingElsewhereLabel))]
    private int _waitingElsewhere;

    /// <summary>How many photographs those answers are about, rather than how many answers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaitingElsewhereLabel))]
    private int _waitingElsewherePhotos;

    /// <summary>
    /// The folder those photographs are in, where they are all in one.
    /// </summary>
    /// <remarks>
    /// Not <c>Folder</c>: that already means the folder this library syncs
    /// through, on this very screen.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WaitingElsewhereLabel))]
    private string _elsewhereFolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ShareCommand))]
    private bool _isBusy;

    /// <summary>
    /// Whether the folder is being asked if it is there.
    /// </summary>
    /// <remarks>
    /// Said on screen, and Sync now is off for as long as it lasts - which is the
    /// whole of the deadline when the folder is asleep or out of reach. The
    /// screen used to say nothing for those seconds, with the button live
    /// underneath, so a press went into a folder that was not there and waited
    /// the same again.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShareCommand))]
    private bool _isChecking;

    public SharingViewModel(IServiceScopeFactory scopeFactory, IActivityLog log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>Asks what to call another computer; set by the window.</summary>
    public IMachineNameQuestion? NameQuestion { get; set; }

    public bool HasFolder => Folder.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool HasMachines => Machines.Count > 0;

    public bool HasOffers => Offers.Count > 0;

    public bool HasNewcomers => Newcomers.Count > 0;

    public bool HasStatus => Status.Length > 0;

    public bool HasWaiting =>
        WaitingOnFaces + WaitingHere + WaitingOnPhotos + WaitingElsewhere > 0;

    public bool HasWaitingOnFaces => WaitingOnFaces > 0;

    public bool HasWaitingHere => WaitingHere > 0;

    public bool HasWaitingOnPhotos => WaitingOnPhotos > 0;

    public bool HasWaitingElsewhere => WaitingElsewhere > 0;

    public bool IsIdle => !IsBusy;

    /// <summary>
    /// Whether a folder is chosen and was there the last time it was asked.
    /// </summary>
    /// <remarks>
    /// What anything on the window's thread goes by, rather than asking the
    /// folder again: asking holds whoever asks for as long as the operating
    /// system takes to give up on a folder that is not answering.
    ///
    /// <para>Only as fresh as the last look, though, which can be hours old on a
    /// screen left open. It is enough to say no; whatever is about to hand the
    /// folder to something that would wait on it asks again first, as Sync now
    /// and <see cref="FolderToBrowseFromAsync"/> do.</para>
    /// </remarks>
    public bool FolderWasThere => HasFolder && !HasProblem && !IsChecking;

    /// <summary>Whether there is anywhere to share, so the button can be pressed.</summary>
    public bool CanShare => IsIdle && FolderWasThere;

    /// <summary>
    /// The one group somebody can do something about: the photographs are on one
    /// computer and nowhere else.
    /// </summary>
    /// <remarks>
    /// Counted in photographs rather than answers, because a folder of
    /// photographs is the thing being asked for. Names the folder where every one
    /// of them is in the same folder, since an instruction to move a folder that
    /// does not say which folder is a riddle.
    /// </remarks>
    public string WaitingElsewhereLabel
    {
        get
        {
            string where = ElsewhereFolder.Length > 0
                ? (WaitingElsewherePhotos == 1
                    ? $", in a folder called \"{ElsewhereFolder}\""
                    : $", all in a folder called \"{ElsewhereFolder}\"")
                : " - they are in a folder only another computer has";

            return WaitingElsewherePhotos == 1
                ? $"1 photo this computer cannot see has a change waiting{where}. Put that "
                  + "folder where every computer can reach it."
                : $"{WaitingElsewherePhotos:N0} photos this computer cannot see have changes "
                  + $"waiting{where}. Put that folder where every computer can reach it.";
        }
    }

    /// <summary>
    /// Answers about photographs that are not where they were, said without
    /// asking for anything.
    /// </summary>
    /// <remarks>
    /// Conditional on purpose. The photograph may turn up and it may have been
    /// renamed for good, and which of the two it is cannot be known from here -
    /// so the sentence says what would happen either way rather than telling
    /// somebody to go and scan.
    /// </remarks>
    public string WaitingOnPhotosLabel =>
        WaitingOnPhotos == 1
            ? "1 change is waiting for a photo that is not in your folders - renamed, moved, "
              + "or not arrived yet. If it turns up under its old name, a scan brings the "
              + "change in."
            : $"{WaitingOnPhotos:N0} changes are waiting for photos that are not in your "
              + "folders - renamed, moved, or not arrived yet. If they turn up under their "
              + "old names, a scan brings the changes in.";

    /// <summary>
    /// Answers waiting on this computer's own face pass, the photograph being
    /// here already.
    /// </summary>
    /// <remarks>
    /// Two different things wear this number: a photograph whose faces have not
    /// been looked for yet, and one where they have and this machine's detector
    /// drew no box where another machine drew one. The first comes good on a
    /// scan and the second never does, so the sentence says the next scan
    /// "may" pick them up rather than promising it.
    ///
    /// <para>It says "scan" and not "sync" deliberately. What lands a held answer
    /// is the scan's own last phase; writing "the next sync will bring them in"
    /// would swap one sentence that cannot come true for another.</para>
    /// </remarks>
    public string WaitingOnFacesLabel =>
        WaitingOnFaces == 1
            ? "1 change is waiting on a face this computer has not found yet. The next scan "
              + "may pick it up."
            : $"{WaitingOnFaces:N0} changes are waiting on faces this computer has not found "
              + "yet. The next scan may pick them up.";

    /// <summary>
    /// Answers about photographs that are here, waiting on something that is not
    /// a face.
    /// </summary>
    /// <remarks>
    /// A turn waits for a small copy of the photograph to turn, and a move waits
    /// for an album this computer does not have - the album somebody kept on
    /// another computer, say, while this one still only suggests it. Both used
    /// to be counted as faces, under a sentence about the face pass that would
    /// never bring either of them in.
    ///
    /// <para>The end of a scan comes first because it is the ordinary case.
    /// Answers land in a scan's last phase, so a scan stopped before it leaves
    /// every answer about a photograph it has just indexed in this group - moves
    /// into albums this computer has, rejections, turns whose small copies were
    /// just made. Saying they wait for something this computer does not have
    /// was false for all of those. The next scan is offered as a possibility,
    /// as the face line offers it, because a move into an album this computer
    /// does not have is not brought in by one.</para>
    /// </remarks>
    public string WaitingHereLabel =>
        WaitingHere == 1
            ? "1 change to a photo that is already here is waiting for the end of a scan, "
              + "a small copy to turn, or an album to put it in. The next scan may pick it "
              + "up."
            : $"{WaitingHere:N0} changes to photos that are already here are waiting for "
              + "the end of a scan, small copies to turn, or albums to put them in. The next "
              + "scan may pick them up.";

    /// <summary>
    /// Looks for answers waiting in the folder, and says nothing when there are
    /// none - including when the folder cannot be reached.
    /// </summary>
    /// <remarks>
    /// <strong>The reason this is worth having at all:</strong> the exchange
    /// writes to the library, so it stays something a person presses. Knowing
    /// whether it is worth pressing should not also be theirs to remember, and a
    /// folder every machine reaches is exactly the kind of thing that goes
    /// quietly out of date while everybody assumes somebody else looked.
    ///
    /// <para><strong>Silent about every failure.</strong> The share sits on a
    /// drive that sleeps, a laptop that is shut, or a network that is not there,
    /// so being unable to reach it is the ordinary state rather than a fault. A
    /// complaint on screen most times the app is opened, about something the
    /// reader already knows and cannot act on, is how a notice teaches people to
    /// close it without reading. Nothing is ever said here except that there is
    /// something to take.</para>
    ///
    /// <para><strong>And it never waits.</strong> Reaching a folder is
    /// <c>Directory.Exists</c> underneath, which cannot be cancelled and which
    /// blocks for as long as the operating system takes to give up on an address
    /// that is not answering - so the look is raced against a deadline and
    /// abandoned if it loses. The thread it leaves behind finishes on its own
    /// long before the next look is due.</para>
    /// </remarks>
    public async Task<string?> LookForUpdatesAsync(TimeSpan patience)
    {
        if (IsBusy)
        {
            return null;
        }

        try
        {
            Task<SharedUpdate> looking = Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                return await scope.ServiceProvider
                    .GetRequiredService<CheckForSharedUpdatesHandler>()
                    .HandleAsync()
                    .ConfigureAwait(false);
            });

            Task first = await Task
                .WhenAny(looking, Task.Delay(patience))
                .ConfigureAwait(true);

            if (first != looking)
            {
                return null;
            }

            SharedUpdate update = await looking.ConfigureAwait(true);

            if (!update.Any || update.Newest <= _declined)
            {
                return null;
            }

            _newest = update.Newest;

            return Asking(update);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            // Looking is not a thing anybody asked for, so it is not a thing
            // anybody should be told has failed.
            _log.Append($"could not look for changes to sync: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    /// Remembers that this much has been answered, however it was answered.
    /// </summary>
    /// <remarks>
    /// Said once the answers have been taken, and once when somebody puts the
    /// question down - both mean the same thing to the next look, which is that
    /// what is in the folder now is not news any more.
    /// </remarks>
    public void Settled() => _declined = _newest;

    private DateTime _newest = DateTime.MinValue;

    /// <summary>
    /// Who has something, in the plainest sentence that is still true.
    /// </summary>
    /// <remarks>
    /// Named where this library has taken from them before, because a name is
    /// what makes a person press the button - and called another computer where
    /// it has not, because the name lives inside a file this check deliberately
    /// does not open.
    ///
    /// <para>A sentence in every arrangement, beginning with a capital letter.
    /// The description used to stand where a name would, so a computer never
    /// taken from began the notice in lower case and said "computer" twice
    /// before it got to the point. With no name in the sentence, "this one" is
    /// enough to say which computer is meant.</para>
    /// </remarks>
    internal static string Asking(SharedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        const string Question = "made some changes. Do you want to sync?";

        if (update.Machines.Count == 0)
        {
            return update.Unnamed == 1
                ? $"Another computer has {Question}"
                : $"Other computers have {Question}";
        }

        List<string> who = [.. update.Machines];

        if (update.Unnamed > 0)
        {
            who.Add(update.Unnamed == 1 ? "another computer" : "other computers");
        }

        return who.Count == 1
            ? $"{who[0]} has {Question}"
            : $"{string.Join(", ", who[..^1])} and {who[^1]} have {Question}";
    }

    /// <summary>Reads the folder, who has shared, and what is waiting.</summary>
    /// <remarks>
    /// Skipped rather than queued when something else is running: the answer is
    /// the same, and a share ends by reading this itself.
    /// </remarks>
    public async Task RefreshAsync()
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await ReadAsync().ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Shares this library through a folder from now on.
    /// </summary>
    /// <remarks>
    /// Refused rather than accepted where the folder overlaps a photo source,
    /// in either direction: sharing writes files into a folder tree, and a scan
    /// would index them as photographs and grow the library a second copy of
    /// itself on every refresh.
    /// </remarks>
    public async Task ChooseFolderAsync(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;

        try
        {
            await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<SetSharedFolderHandler>()
                    .HandleAsync(folder)
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            Status = string.Empty;
            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            // Every reason this fails is one the user can do something about, so
            // none of them may be reported as a bare failure.
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops syncing, keeping everything already brought in.
    /// </summary>
    /// <remarks>
    /// Nothing is taken out of the folder here. Whether this computer's files
    /// should go with it is asked separately, by the window, the same way it is
    /// asked when somebody chooses a different folder.
    /// </remarks>
    public async Task StopSyncingAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;

        try
        {
            await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<SetSharedFolderHandler>()
                    .ClearAsync()
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            Status = "This computer has stopped syncing. Choose a folder to start again.";
            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"Syncing could not be stopped: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Whether this library has stopped syncing through a folder it used to,
    /// so that its files there are worth offering to remove.
    /// </summary>
    /// <remarks>
    /// Judged on what the screen read after the change rather than on what was
    /// asked for: a folder that was refused leaves the old one in place, and
    /// choosing the same folder again leaves nothing behind.
    /// </remarks>
    public bool HasLeft(string before) =>
        !string.IsNullOrWhiteSpace(before)
        && !string.Equals(before, Folder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Takes this computer's own files out of a folder it no longer syncs
    /// through, and says what came of it.
    /// </summary>
    /// <remarks>
    /// Off the window's thread, because the folder is usually on another
    /// machine, and one that has gone away holds whoever asks it. The small
    /// copies of photographs are left and the sentence says so, since they are
    /// named after the photograph and another computer may have left the same
    /// ones.
    /// </remarks>
    public async Task RemoveOwnFilesAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;

        try
        {
            int removed = await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                return await scope.ServiceProvider
                    .GetRequiredService<RemoveOwnSyncFilesHandler>()
                    .HandleAsync(folder)
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            Status = removed == 0
                ? $"There was nothing of this computer's left in {folder}."
                : $"This computer's files have been removed from {folder}. The small copies of "
                  + "photos stay, because other computers may have left the same ones.";

            _log.Append($"removed {removed} of this computer's files from {folder}");
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"This computer's files could not be removed from {folder}: {ex.Message}";
            _log.Append($"could not remove this computer's files from {folder}: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Where the folder picker opens: at the sync folder where a look made just
    /// now finds it there, and nowhere in particular otherwise.
    /// </summary>
    /// <remarks>
    /// <strong>Asked again, not remembered.</strong> The picker refuses to open,
    /// silently, at a folder that is not there; handed an address on a network
    /// that has gone, it holds the window's thread while the shell gives up on
    /// it, and can then throw. What the screen's last look found can be hours
    /// old - the drive switched off, or the laptop asleep and woken out of the
    /// house, with the screen open throughout - and the picker going by it was
    /// handed exactly that address. So a folder last found there is asked again
    /// first, by the screen's own read: off the window's thread, under the
    /// deadline everything else that asks it keeps, and saying on screen that it
    /// is checking.
    ///
    /// <para>"Change..." is off while it asks, so a second press cannot put a
    /// second picker over the first once the folder answers. A folder the last
    /// look found gone, or is still asking, is not asked again: the picker opens
    /// at once, which is what somebody choosing a different folder wants. Nor is
    /// one somebody else is asking or writing to at that moment - the picker does
    /// not wait behind them, and does not go by an answer it has not heard.</para>
    /// </remarks>
    public async Task<string> FolderToBrowseFromAsync()
    {
        if (!FolderWasThere || !await _gate.WaitAsync(0).ConfigureAwait(true))
        {
            return string.Empty;
        }

        IsBusy = true;

        try
        {
            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            // The picker opens all the same, since choosing a folder is what was
            // asked for; only where it opens could not be found out.
            _log.Append($"could not look in the sync folder before choosing another: {ex.Message}");

            return string.Empty;
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }

        return FolderWasThere ? Folder : string.Empty;
    }

    /// <summary>
    /// Takes everybody's answers, then gives this library's back.
    /// </summary>
    /// <remarks>
    /// That order is what makes three machines converge with no machinery for
    /// it: this library's file carries what it has just been told as well as
    /// what it decided itself.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanShare))]
    private async Task ShareAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;
        Status = string.Empty;

        try
        {
            // Asked again first, by the screen's own read and under the same
            // deadline. The folder that answered when the screen opened may
            // have gone since; found gone here, it is said in red and keeps the
            // button off until a later look finds it, rather than being
            // reported once in a line about a share that never started.
            await ReadAsync().ConfigureAwait(true);

            if (!FolderWasThere)
            {
                // Written down all the same, as it was when the share itself
                // found the folder gone: the red line goes at the next look, and
                // the log is then the only trace that the press was made.
                Record(Problem);
                return;
            }

            ShareResult result = await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                return await scope.ServiceProvider
                    .GetRequiredService<ShareNowHandler>()
                    .HandleAsync()
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            Status = result.Summary;
            Record(result.Published.Published
                ? result.Summary
                : $"{result.Summary} ({result.Published.Problem})");

            Offers =
            [
                .. result.Offers.Select(offer => new PairingOffer(
                    offer.Mine.SharedId,
                    offer.Theirs.SharedId,
                    $"{offer.CalledHere} keeps photos in {offer.Theirs.Root}. "
                  + $"Is that the same folder as {offer.Mine.Root}?")),
            ];

            // Named rather than swallowed. A smaller exchange reported as a
            // complete one is the kind of quiet wrong this feature cannot
            // afford, and a file being written as this one read is the ordinary
            // cause - which comes good on the next press.
            if (result.Merged.Unreadable.Count > 0)
            {
                Status += result.Merged.Unreadable.Count == 1
                    ? " One computer's changes could not be read this time."
                    : $" {result.Merged.Unreadable.Count} computers' changes could not be read "
                      + "this time.";
            }

            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"The sync could not finish: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// Writes down which folder a press of Sync now went through, and what came
    /// of it.
    /// </summary>
    /// <remarks>
    /// The folder first, and on its own line, because the failure this records
    /// is two machines each sharing faultlessly through a folder the other cannot
    /// see. Two logs side by side name it in a second; without them both laptops
    /// only ever said everything was fine.
    /// </remarks>
    private void Record(string outcome)
    {
        _log.Append($"syncing through {Folder}");
        _log.Append($"  {outcome}");
    }

    /// <summary>
    /// Records that two folders, reached two ways, are the same folder.
    /// </summary>
    /// <remarks>
    /// The one step in this feature nobody can work out for the user. A UNC path
    /// and a mapped drive letter are the same place and nothing in the text says
    /// so, and absorbing two unrelated folders into one identity would file
    /// every photograph in each under a key meaning a different photograph in
    /// the other. So it is asked, once, and answered by a person.
    /// </remarks>
    [RelayCommand]
    private async Task PairAsync(PairingOffer? offer)
    {
        if (offer is null)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;

        try
        {
            await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<ConfirmPairingHandler>()
                    .HandleAsync(offer.Mine, offer.Theirs)
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            // Taken off the screen whether or not the next share happens, so an
            // answered question does not sit there being asked again.
            Offers = [.. Offers.Where(other => other != offer)];
            Status = "Those two folders are one from now on. Sync again to bring "
                   + "everybody's changes across.";

            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"Those folders could not be paired: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>Takes a computer's changes from now on, as somebody here said.</summary>
    /// <remarks>
    /// Asked once per computer, the way whether two folders are one is asked,
    /// because every check a sync makes is on what a computer's own file says
    /// about it - and a file in the sync folder can say anything.
    /// </remarks>
    [RelayCommand]
    private Task TakeChangesAsync(MachineQuestion? question) =>
        question is null
            ? Task.CompletedTask
            : DecideAsync(
                question.MachineId,
                question.Name,
                take: true,
                $"This computer will sync with {question.Name} from now on. Sync again to "
                + "bring its changes in.");

    /// <summary>Leaves a computer's changes alone, and keeps leaving them.</summary>
    [RelayCommand]
    private Task NotThisOneAsync(MachineQuestion? question) =>
        question is null
            ? Task.CompletedTask
            : DecideAsync(
                question.MachineId,
                question.Name,
                take: false,
                $"This computer will not sync with {question.Name}. You can change your "
                + "mind under The other computers.");

    /// <summary>
    /// Takes the changes of a computer somebody earlier said "not this one" to.
    /// </summary>
    /// <remarks>
    /// So a "no" said in haste about the family's own laptop is not the end of
    /// that laptop.
    /// </remarks>
    [RelayCommand]
    private Task TakeChangesAfterAllAsync(MachineRow? row) =>
        row is null
            ? Task.CompletedTask
            : DecideAsync(
                row.MachineId,
                row.Name,
                take: true,
                $"This computer will sync with {row.Name} from now on. Sync again to bring "
                + "its changes in.");

    /// <summary>
    /// Asks what to call another computer here, until a name is kept or the
    /// question is cancelled.
    /// </summary>
    /// <remarks>
    /// A name another computer already goes by is asked again with the reason
    /// under the box and the name still in it, rather than kept: two rows
    /// reading the same are what somebody answers for twice.
    /// </remarks>
    [RelayCommand]
    private async Task RenameMachineAsync(MachineRow? row)
    {
        if (row?.OwnName is not string ownName || NameQuestion is not IMachineNameQuestion question)
        {
            return;
        }

        string typed = row.NameToEdit ?? ownName;
        string? problem = null;

        while (question.AskName(typed, ownName, problem) is string answer)
        {
            MachineNaming? naming = await NameAsync(row.MachineId, answer).ConfigureAwait(true);

            if (naming != MachineNaming.NameTaken)
            {
                return;
            }

            typed = answer;
            problem = "Another computer is already called "
                      + $"{KnownMachine.FamiliarNameFrom(answer, ownName) ?? ownName}.";
        }
    }

    /// <summary>
    /// Keeps a name for another computer, and reads the screen again.
    /// </summary>
    /// <returns>How it went, or null where it could not be saved and the status says why.</returns>
    private async Task<MachineNaming?> NameAsync(Guid machineId, string typed)
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;

        try
        {
            MachineNaming naming = await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                return await scope.ServiceProvider
                    .GetRequiredService<NameMachineHandler>()
                    .HandleAsync(machineId, typed)
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            if (naming == MachineNaming.Named)
            {
                await ReadAsync().ConfigureAwait(true);
            }

            return naming;
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"That could not be saved: {ex.Message}";
            return null;
        }
        catch (ArgumentException ex)
        {
            Status = $"That could not be saved: {ex.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    private async Task DecideAsync(Guid machineId, string name, bool take, string said)
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;

        try
        {
            await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<DecideMachineHandler>()
                    .HandleAsync(machineId, name, take)
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            Status = said;
            _log.Append(take ? $"syncing with {name}" : $"not syncing with {name}");

            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"That could not be saved: {ex.Message}";
        }
        catch (ArgumentException ex)
        {
            // A file in the sync folder claiming to be this computer, or to be
            // nobody: said, not thrown past the button, where it would close
            // the app.
            Status = $"That could not be saved: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>
    /// What the screen asks about a computer nobody here has answered for.
    /// </summary>
    /// <remarks>
    /// A name any other computer here goes by is said out loud, whatever was
    /// answered about that one, because that is exactly what a planted file
    /// would choose: the question then reads as the laptop on the kitchen
    /// table, and a yes to it is a yes to whatever wrote the file. On a new or
    /// reinstalled laptop the real one is unanswered too, so the two questions
    /// would otherwise read the same - each says when that computer last
    /// shared, which is what tells them apart.
    /// </remarks>
    internal static string AskingAbout(MachineStanding newcomer)
    {
        ArgumentNullException.ThrowIfNull(newcomer);

        if (!newcomer.SharesItsName)
        {
            return $"{newcomer.Name} has left changes in the sync folder.";
        }

        string when = newcomer.SharedUtc is DateTime shared
            ? string.Format(
                CultureInfo.CurrentCulture,
                "last shared changes on {0:d MMM yyyy 'at' HH:mm}",
                shared.ToLocalTime())
            : "has not shared any changes yet";

        // By the name the two share, which may be what a computer calls itself
        // or what somebody here calls one.
        string alike = newcomer.NameItShares ?? newcomer.Name;

        return $"Another computer here also goes by {alike}. Sync with this one, which "
               + $"{when}? If you cannot tell which is the real {alike}, choose Don't sync "
               + "with this computer. You can change your mind under The other computers.";
    }

    /// <summary>The read itself, without the gate, so callers holding it can use it.</summary>
    /// <remarks>
    /// In two steps. The folder's name comes from this library and is there at
    /// once, so the screen names the folder it is checking and says it is
    /// checking; then the folder is asked, which takes as long as the deadline
    /// when nothing answers. What the last look said is put away meanwhile,
    /// because a red line beside "checking" would be two answers at once.
    /// </remarks>
    private async Task ReadAsync()
    {
        Folder = await ReadingAsync(sharing => sharing.FolderAsync()).ConfigureAwait(true);
        Problem = string.Empty;
        IsChecking = HasFolder;

        SharingStatus status;

        try
        {
            status = await ReadingAsync(sharing => sharing.HandleAsync()).ConfigureAwait(true);
        }
        finally
        {
            IsChecking = false;
        }

        DateTime now = DateTime.UtcNow;

        Folder = status.Folder;
        Problem = status.Problem;
        WaitingOnFaces = status.Held.Faces.Answers;
        WaitingHere = status.Held.Here.Answers;
        WaitingOnPhotos = status.Held.Missing.Answers;
        WaitingElsewhere = status.Held.Elsewhere.Answers;
        WaitingElsewherePhotos = status.Held.Elsewhere.Photos;
        ElsewhereFolder = status.Held.Folder;
        Machines =
        [
            .. status.Machines.Select(machine =>
            {
                bool ignored = machine.Trust == MachineTrust.Ignored;

                return new MachineRow(
                    machine.Name,
                    ignored ? "not syncing" : machine.Recency(now),
                    !machine.Merged && !ignored)
                {
                    MachineId = machine.MachineId,
                    Ignored = ignored,
                    OwnName = machine.OwnName,
                    FamiliarName = machine.FamiliarName,
                };
            }),
        ];
        Newcomers =
        [
            .. status.Machines
                .Where(machine => machine.IsUnanswered)
                .Select(machine => new MachineQuestion(
                    machine.MachineId, machine.Name, AskingAbout(machine))),
        ];
    }

    /// <summary>
    /// One question of what the screen shows, off the window's thread and in a
    /// scope of its own.
    /// </summary>
    private Task<T> ReadingAsync<T>(Func<GetSharingHandler, Task<T>> read) =>
        Task.Run(async () =>
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            return await read(scope.ServiceProvider.GetRequiredService<GetSharingHandler>())
                .ConfigureAwait(false);
        });
}
