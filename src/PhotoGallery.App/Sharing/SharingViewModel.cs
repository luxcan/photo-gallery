using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.Application.UseCases.Sharing;

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
    /// One job at a time.
    /// </summary>
    /// <remarks>
    /// A read is raised by opening the screen, which can happen while a share is
    /// still running - and both of them touch the same folder.
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

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

    public SharingViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
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
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or InvalidOperationException
                                      or DirectoryNotFoundException)
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
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or InvalidOperationException)
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
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or InvalidOperationException
                                      or ArgumentException)
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
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or InvalidOperationException)
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
