using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.Application.Ports;
using PhotoGallery.Application.UseCases.Sharing;

using PhotoGallery.App.Shell;

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
    /// What the notice over the app is saying, or empty while it says nothing.
    /// </summary>
    /// <remarks>
    /// Two things in one panel, because they are two halves of one exchange: a
    /// question when another computer has published something this library has
    /// not taken, and the answer to that question once it has been taken. A
    /// second panel for the summary would appear exactly where the first one
    /// had been, half a minute later, saying something about the same press.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = string.Empty;

    /// <summary>
    /// Whether the notice is asking something, rather than reporting it.
    /// </summary>
    /// <remarks>
    /// A question carries the button that answers it. A report carries only the
    /// way to put it down, because the thing it reports has already happened.
    /// </remarks>
    [ObservableProperty]
    private bool _noticeAsks;

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

    /// <summary>What the last share did, or why it could not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    /// <summary>
    /// Answers waiting for photographs this library has not indexed.
    /// </summary>
    /// <remarks>
    /// The one number this screen must never hide. It is the difference between
    /// "nothing to do" and "an evening's work is waiting for a folder nobody has
    /// added", and nothing else on the screen would ever hint at it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaiting), nameof(WaitingLabel))]
    private int _waiting;

    /// <summary>
    /// Photographs this library has indexed and not prepared.
    /// </summary>
    /// <remarks>
    /// What the pictures half is worth, in the only unit that means anything to
    /// somebody deciding whether to spend five minutes: how many photographs
    /// would otherwise be read one at a time.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnprepared), nameof(PicturesLabel), nameof(CanTakePictures))]
    [NotifyCanExecuteChangedFor(nameof(TakePicturesCommand))]
    private int _unprepared;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanTakePictures))]
    [NotifyCanExecuteChangedFor(nameof(ShareCommand), nameof(TakePicturesCommand))]
    private bool _isBusy;

    public SharingViewModel(IServiceScopeFactory scopeFactory, IActivityLog log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public bool HasFolder => Folder.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool HasMachines => Machines.Count > 0;

    public bool HasOffers => Offers.Count > 0;

    /// <summary>
    /// Whether there are photographs here that another machine may already have
    /// prepared.
    /// </summary>
    public bool CanTakePictures => CanShare && Unprepared > 0;

    public bool HasStatus => Status.Length > 0;

    public bool HasWaiting => Waiting > 0;

    public bool HasUnprepared => Unprepared > 0;

    public bool IsIdle => !IsBusy;

    /// <summary>Whether there is anywhere to share, so the button can be pressed.</summary>
    public bool CanShare => IsIdle && HasFolder && !HasProblem;

    /// <summary>
    /// What taking the pictures would save, and what it costs, said before the
    /// click rather than after.
    /// </summary>
    /// <remarks>
    /// The rate is this library's own measurement: about 24.8 GB read for 15,823
    /// photographs, at roughly six a second on a network share. Copying the two
    /// small renditions instead runs at about fifty a second. Rounded hard,
    /// because it is a decision aid and not a promise.
    ///
    /// <para>Rounding that hard means the two costs land on the same words
    /// whenever there is only a handful outstanding - fourteen photographs is
    /// two seconds against a quarter of one, and both are "a minute". Said as a
    /// comparison, that offered a choice and quoted the same figure for either
    /// answer, which reads as a mistake and hides the one thing the sentence is
    /// for. So when the two round together it is stated once, as the honest
    /// answer that it hardly matters.</para>
    /// </remarks>
    public string PicturesLabel
    {
        get
        {
            string making = Roughly(TimeSpan.FromSeconds(Unprepared / 6.0));
            string copying = Roughly(TimeSpan.FromSeconds(Unprepared / 50.0));

            string cost = making == copying
                ? "Taking the ones another computer has already made, or making them from "
                  + $"your own files, is about {making} either way."
                : $"Making them from your own files takes about {making}; taking the ones "
                  + $"another computer has already made takes about {copying}.";

            return $"{Unprepared:N0} {(Unprepared == 1 ? "photo has" : "photos have")} no small "
                 + $"copy here yet. {cost} Your photographs themselves are never copied.";
        }
    }

    public string WaitingLabel =>
        Waiting == 1
            ? "1 answer is waiting for a photo this library has not indexed yet. "
              + "Scanning will bring it in."
            : $"{Waiting:N0} answers are waiting for photos this library has not indexed yet. "
              + "Scanning will bring them in.";

    public bool HasNotice => Notice.Length > 0;

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
    public async Task LookForUpdatesAsync(TimeSpan patience)
    {
        if (IsBusy || NoticeAsks)
        {
            return;
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
                return;
            }

            SharedUpdate update = await looking.ConfigureAwait(true);

            if (!update.Any || update.Newest <= _declined)
            {
                return;
            }

            _newest = update.Newest;
            NoticeAsks = true;
            Notice = Asking(update.Machines);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            // Looking is not a thing anybody asked for, so it is not a thing
            // anybody should be told has failed.
            _log.Append($"could not look for shared answers: {ex.Message}");
        }
    }

    /// <summary>Puts the notice down, and says nothing more about this one.</summary>
    [RelayCommand]
    private void DismissNotice()
    {
        if (NoticeAsks)
        {
            _declined = _newest;
        }

        Notice = string.Empty;
        NoticeAsks = false;
    }

    /// <summary>Says what the notice reports once the answers have been taken.</summary>
    public void Report(string summary)
    {
        _declined = _newest;
        NoticeAsks = false;
        Notice = summary;
    }

    private DateTime _newest = DateTime.MinValue;

    /// <summary>
    /// Who has something, in the plainest sentence that is still true.
    /// </summary>
    /// <remarks>
    /// Named where this library has taken from them before, because a name is
    /// what makes a person press the button - and described where it has not,
    /// because the name lives inside a file this check deliberately does not
    /// open.
    /// </remarks>
    private static string Asking(IReadOnlyList<string> machines) =>
        machines.Count == 1
            ? $"{machines[0]} has answers this computer has not taken yet."
            : $"{string.Join(" and ", machines)} have answers this computer has not taken yet.";

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
            ShareResult result = await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                return await scope.ServiceProvider
                    .GetRequiredService<ShareNowHandler>()
                    .HandleAsync()
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            Status = result.Summary;

            // The folder first, and on its own line, because the failure this
            // records is two machines each sharing faultlessly through a folder
            // the other cannot see. Two logs side by side name it in a second;
            // without them both laptops only ever said everything was fine.
            _log.Append($"sharing through {Folder}");
            _log.Append($"  {result.Summary}");

            Offers =
            [
                .. result.Offers.Select(offer => new PairingOffer(
                    offer.Mine.SharedId,
                    offer.Theirs.SharedId,
                    $"{offer.MachineName} keeps photos in {offer.Theirs.Root}. "
                  + $"Is that the same folder as {offer.Mine.Root}?")),
            ];

            // Named rather than swallowed. A smaller exchange reported as a
            // complete one is the kind of quiet wrong this feature cannot
            // afford, and a file being written as this one read is the ordinary
            // cause - which comes good on the next press.
            if (result.Merged.Unreadable.Count > 0)
            {
                Status += result.Merged.Unreadable.Count == 1
                    ? " One computer's answers could not be read this time."
                    : $" {result.Merged.Unreadable.Count} computers' answers could not be read "
                      + "this time.";
            }

            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"Sharing could not finish: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
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
            Status = "Those two folders are one from now on. Share again to bring "
                   + "everybody's answers across.";

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

    /// <summary>
    /// Takes the small copies another computer has already made, and leaves this
    /// library's for the ones that follow.
    /// </summary>
    /// <remarks>
    /// Its own button, and deliberately not part of Share now. The answers are a
    /// small file and seconds; the pictures are gigabytes and minutes, and the
    /// first is worth doing every day while the second is worth doing once. A
    /// machine that wants the decisions and not the gigabytes gets exactly that
    /// by not pressing this.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanTakePictures))]
    private async Task TakePicturesAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;
        Status = string.Empty;

        try
        {
            PoolResult result = await Task.Run(async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                return await scope.ServiceProvider
                    .GetRequiredService<ShareRenditionsHandler>()
                    .HandleAsync()
                    .ConfigureAwait(false);
            }).ConfigureAwait(true);

            Status = result.Summary;
            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (LibraryFailure.IsExpected(ex))
        {
            Status = $"The pictures could not be copied: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    /// <summary>A duration as somebody deciding whether to wait would say it.</summary>
    private static string Roughly(TimeSpan span) => span switch
    {
        { TotalSeconds: < 90 } => "a minute",
        { TotalMinutes: < 90 } => $"{Math.Round(span.TotalMinutes):N0} minutes",
        { TotalHours: < 2 } => "an hour",
        _ => $"{Math.Round(span.TotalHours):N0} hours",
    };

    /// <summary>The read itself, without the gate, so callers holding it can use it.</summary>
    private async Task ReadAsync()
    {
        SharingStatus status = await Task.Run(async () =>
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            return await scope.ServiceProvider
                .GetRequiredService<GetSharingHandler>()
                .HandleAsync()
                .ConfigureAwait(false);
        }).ConfigureAwait(true);

        DateTime now = DateTime.UtcNow;

        Folder = status.Folder;
        Problem = status.Problem;
        Waiting = status.Waiting;
        Unprepared = status.Unprepared;
        Machines =
        [
            .. status.Machines.Select(machine => new MachineRow(
                machine.Name, machine.Recency(now), !machine.Merged)),
        ];
    }
}
