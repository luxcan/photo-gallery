using PhotoGallery.Application.Ports;
using PhotoGallery.Domain.Library;
using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Application.UseCases.Sharing;

/// <summary>
/// Looks for answers waiting in the shared folder, without taking any.
/// </summary>
/// <remarks>
/// So that nobody has to remember to go and look. The exchange is deliberately
/// something a person presses, because it writes to the library and a write
/// nobody asked for is a write nobody can undo - but knowing whether it is worth
/// pressing should not itself be a chore, and a folder every machine can reach
/// is exactly the kind of thing that goes quietly out of date.
///
/// <para><strong>It reads and compares, and writes nothing at all.</strong> A
/// directory listing gives when each machine last published; KnownMachines gives
/// when this library last took from each of them. Newer means there is
/// something to take. No decision is read, no file decompressed, and nothing is
/// recorded about having looked.</para>
///
/// <para><strong>Silence is the ordinary answer, including when the folder
/// cannot be reached.</strong> The share lives on a drive that sleeps, a laptop
/// that is shut, or a network that is not there, so failing to reach it is the
/// common case rather than a fault, and this reports the same nothing it
/// reports when the folder is reachable and empty. Anything louder would put a
/// complaint on screen most times the app is opened, about a state the user
/// already understands and cannot act on.</para>
///
/// <para><strong>A caller running this in the background must bound it in
/// time.</strong> Reaching a folder is <c>Directory.Exists</c> underneath, which
/// cannot be cancelled and which blocks for as long as the operating system
/// takes to give up on an address that is not answering. This handler does not
/// bound that, because the Sharing screen wants the real answer however long it
/// takes; a periodic check does not, and its caller says so.</para>
/// </remarks>
public sealed class CheckForSharedUpdatesHandler
{
    private readonly ILibraryIndex _index;
    private readonly IDecisionReader _decisions;
    private readonly IDecisionExchange _exchange;

    public CheckForSharedUpdatesHandler(
        ILibraryIndex index,
        IDecisionReader decisions,
        IDecisionExchange exchange)
    {
        _index = index;
        _decisions = decisions;
        _exchange = exchange;
    }

    public async Task<SharedUpdate> HandleAsync(CancellationToken cancellationToken = default)
    {
        LibrarySettings settings =
            await _index.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(settings.SharedFolder))
        {
            return SharedUpdate.Nothing;
        }

        // The listing, which is also the reachability test: a folder that cannot
        // be reached answers with nothing rather than throwing, and nothing is
        // what this reports.
        IReadOnlyList<PublishedAnswers> published =
            await _exchange.StandingAsync(cancellationToken).ConfigureAwait(false);

        if (published.Count == 0)
        {
            return SharedUpdate.Nothing;
        }

        IReadOnlyList<KnownMachine> known =
            await _decisions.KnownMachinesAsync(cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, KnownMachine> taken = [];
        foreach (KnownMachine machine in known)
        {
            taken[machine.MachineId] = machine;
        }

        List<string> names = [];
        DateTime newest = DateTime.MinValue;

        foreach (PublishedAnswers answers in published)
        {
            // Our own file. A machine is never news to itself, and its own date
            // moves every time it publishes.
            if (answers.MachineId == settings.MachineId)
            {
                continue;
            }

            if (taken.TryGetValue(answers.MachineId, out KnownMachine? machine))
            {
                // Written before the moment this library last took from it is
                // a file this library has already read. The same moment counts
                // as new, which is the whole of this check's bias: a machine
                // that published in the same instant as the merge that read it
                // costs one wasted press if it is offered again, and costs an
                // answer that never arrives if it is not.
                if (machine.LastMergedUtc is DateTime last && answers.WrittenUtc < last)
                {
                    continue;
                }

                names.Add(machine.Name);
            }
            else
            {
                // A machine that has published and never been taken from. Its
                // name lives inside its file, which this deliberately does not
                // open, so it is described rather than named - the same words
                // the Sharing screen uses for the same machine.
                names.Add("a computer this library has not taken answers from yet");
            }

            if (answers.WrittenUtc > newest)
            {
                newest = answers.WrittenUtc;
            }
        }

        return names.Count == 0
            ? SharedUpdate.Nothing
            : new SharedUpdate([.. names.Distinct(StringComparer.CurrentCultureIgnoreCase).Order()], newest);
    }
}
