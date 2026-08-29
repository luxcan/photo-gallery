using System.Text.Json;
using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// Answers exchanged through a folder every machine in the house can already
/// reach.
/// </summary>
/// <remarks>
/// The obvious reading of "sync between laptops" is a direct connection, and it
/// is not what ships first. The deciding fact is that a shared folder
/// <strong>does not need the other laptop switched on</strong> - and a family
/// will not get two laptops open at the same moment on purpose. A design that
/// requires it is a design that gets used twice and then forgotten. It also
/// works on a network profile set to Public, which blocks inbound traffic
/// outright and is what Windows chooses by default.
///
/// <para>Each machine writes one file, named after itself, and reads everybody
/// else's. Written whole and renamed into place, so a reader never sees half a
/// file. There is no locking and none is needed: one writer per file, and a
/// reader that catches a rename sees the old copy and picks up the new one next
/// time.</para>
/// </remarks>
public sealed class SharedFolderExchange : IDecisionExchange
{
    /// <summary>Everything this feature writes goes under here, and nothing else does.</summary>
    public const string AnswersFolder = "answers";

    private const string TempExtension = ".tmp";

    private readonly ILibraryIndex _index;

    public SharedFolderExchange(ILibraryIndex index) => _index = index;

    public async Task<ExchangeReadiness> ReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(settings.SharedFolder))
        {
            return ExchangeReadiness.Not(
                "Choose a folder that every computer in the house can reach.");
        }

        if (!Directory.Exists(settings.SharedFolder))
        {
            return ExchangeReadiness.Not(
                $"That folder cannot be reached at the moment: {settings.SharedFolder}");
        }

        return ExchangeReadiness.Ready;
    }

    public async Task PublishAsync(
        DecisionSet mine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mine);

        string answers = await AnswersPathAsync(cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(answers);

        string final = Path.Combine(answers, NameOf(mine.Machine.Id));
        string temporary = final + TempExtension;

        // Written whole, flushed, then renamed. Three laptops read this folder
        // whenever they like, and a half-written file is one that parses as far
        // as the truncation and then throws - or worse, does not.
        await using (var file = new FileStream(
            temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await DecisionSetFile.WriteAsync(file, mine, cancellationToken).ConfigureAwait(false);
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, final, overwrite: true);
    }

    public async Task<FetchedDecisions> FetchAsync(CancellationToken cancellationToken = default)
    {
        string answers = await AnswersPathAsync(cancellationToken).ConfigureAwait(false);

        if (!Directory.Exists(answers))
        {
            // Nobody has published anything yet, which is not a fault - it is
            // what the first machine in the house always sees.
            return FetchedDecisions.None;
        }

        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        string ours = NameOf(settings.MachineId);

        List<DecisionSet> sets = [];
        List<UnreadableAnswers> unreadable = [];

        foreach (string path in Directory.EnumerateFiles(answers, "*" + DecisionSetFile.Extension))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = Path.GetFileName(path);

            // Our own file, which we wrote. The merge would skip it anyway; not
            // reading it saves half a megabyte and a decompression.
            if (string.Equals(name, ours, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await using var file = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                sets.Add(await DecisionSetFile
                    .ReadAsync(file, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or InvalidDataException
                                          or FormatException)
            {
                // One bad file must not cost the exchange every good one, and
                // must not pass in silence either. A machine writing at the
                // moment this one read is the ordinary case, and it comes good
                // on the next run.
                unreadable.Add(new UnreadableAnswers(name, ex.Message));
            }
        }

        return new FetchedDecisions(sets, unreadable);
    }

    public async Task<IReadOnlyList<PublishedAnswers>> StandingAsync(
        CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(settings.SharedFolder))
        {
            return [];
        }

        string answers = Path.Combine(settings.SharedFolder, AnswersFolder);

        if (!Directory.Exists(answers))
        {
            return [];
        }

        List<PublishedAnswers> standing = [];

        // A listing and nothing else. The name is the machine and the file's own
        // last-write time is when that machine last shared, so this reads no
        // bytes at all - which is what lets the screen ask it on every open.
        foreach (string path in Directory.EnumerateFiles(answers, "*" + DecisionSetFile.Extension))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = Path.GetFileNameWithoutExtension(
                Path.GetFileNameWithoutExtension(path));

            // A file somebody dropped in by hand, or one a later version writes
            // under a different name. Left alone rather than guessed at.
            if (!Guid.TryParse(name, out Guid machine))
            {
                continue;
            }

            standing.Add(new PublishedAnswers(machine, File.GetLastWriteTimeUtc(path)));
        }

        return standing;
    }

    /// <summary>
    /// A machine's file is named after the machine, which is what makes one
    /// writer per file true.
    /// </summary>
    private static string NameOf(Guid machine) => $"{machine:D}{DecisionSetFile.Extension}";

    private async Task<string> AnswersPathAsync(CancellationToken cancellationToken)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(settings.SharedFolder))
        {
            throw new InvalidOperationException(
                "No shared folder has been chosen for this library.");
        }

        return Path.Combine(settings.SharedFolder, AnswersFolder);
    }
}
