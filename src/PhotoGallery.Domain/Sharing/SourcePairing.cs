namespace PhotoGallery.Domain.Sharing;

/// <summary>
/// Working out which of two machines' folders are the same folder.
/// </summary>
/// <remarks>
/// <strong>Matching roots is not string equality, and must not be built as
/// though it were.</strong> <c>\\192.168.50.103\PhotoGallery</c> on one laptop
/// and <c>Z:\</c> on another are the same folder reached two ways, and Windows
/// offers to map that drive letter for you - so comparing the text would lock
/// out a family member for doing an entirely normal thing, with nothing in the
/// app to undo it.
///
/// <para>So: the likely pairs are proposed, a person confirms once, and the
/// confirmation is published like any other decision. Nothing here decides
/// anything on its own.</para>
///
/// <para>Pure, like the merge, and for the same reason: every rule in it is
/// about two machines disagreeing about what a folder is called, and that is
/// worth arguing out against tests rather than against two real network
/// shares.</para>
/// </remarks>
public static class SourcePairing
{
    /// <summary>
    /// The pairs worth putting to somebody, and the mistakes worth naming.
    /// </summary>
    /// <param name="mine">This library's own folders.</param>
    /// <param name="theirs">One other machine's folders, and what it calls itself.</param>
    /// <param name="linked">
    /// Pairs already settled. A folder that is already paired is not proposed
    /// again, and neither is the machine's own id.
    /// </param>
    public static IReadOnlyList<PairingProposal> Propose(
        IReadOnlyList<SharedSource> mine,
        IReadOnlyList<SharedSource> theirs,
        string machineName,
        IReadOnlyCollection<SourceLink> linked)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(theirs);
        ArgumentNullException.ThrowIfNull(linked);

        HashSet<(Guid, Guid)> settled =
            [.. linked.Select(link => (link.Ordered().Left, link.Ordered().Right))];

        List<PairingProposal> proposals = [];

        foreach (SharedSource ours in mine)
        {
            foreach (SharedSource them in theirs)
            {
                // Already one folder, by id. Nothing to propose and nothing
                // wrong.
                if (ours.SharedId == them.SharedId)
                {
                    continue;
                }

                (Guid, Guid) pair = ours.SharedId.CompareTo(them.SharedId) <= 0
                    ? (ours.SharedId, them.SharedId)
                    : (them.SharedId, ours.SharedId);

                if (settled.Contains(pair))
                {
                    continue;
                }

                if (Likeness(ours.Root, them.Root) is PairingLikeness likeness)
                {
                    proposals.Add(new PairingProposal(ours, them, machineName, likeness));
                }
            }
        }

        // The surest first, so a screen showing one shows the best one; and the
        // diagnosis last, because it is the only entry nobody can act on here.
        return [.. proposals.OrderBy(proposal => proposal.Likeness)];
    }

    /// <summary>
    /// What this library must rename its own sources to, given every link it
    /// now knows about.
    /// </summary>
    /// <remarks>
    /// Followed through rather than applied once. Three machines can pair
    /// pairwise - A to B, B to C - and the third link is one nobody ever
    /// confirmed; following the chain to its lowest id is what makes all three
    /// land on one identity without anybody being asked twice.
    /// </remarks>
    public static IReadOnlyDictionary<Guid, Guid> Adopt(
        IReadOnlyCollection<Guid> mine, IReadOnlyCollection<SourceLink> links)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(links);

        // Everything joined to everything it is joined to, however far round.
        Dictionary<Guid, HashSet<Guid>> joined = [];

        foreach (SourceLink link in links)
        {
            Join(joined, link.Left, link.Right);
            Join(joined, link.Right, link.Left);
        }

        Dictionary<Guid, Guid> renames = [];

        foreach (Guid source in mine)
        {
            if (!joined.ContainsKey(source))
            {
                continue;
            }

            Guid lowest = Reach(joined, source).Min();

            if (lowest != source)
            {
                renames[source] = lowest;
            }
        }

        return renames;
    }

    /// <summary>
    /// Why two roots look alike, or null where they do not.
    /// </summary>
    /// <remarks>
    /// Deliberately shallow. This is the input to a question somebody answers,
    /// not the answer: a cleverer rule that got it right more often would still
    /// have to ask, and would be harder to explain when it was wrong.
    /// </remarks>
    private static PairingLikeness? Likeness(string mine, string theirs)
    {
        string ours = Trim(mine);
        string them = Trim(theirs);

        if (string.Equals(ours, them, StringComparison.OrdinalIgnoreCase))
        {
            return PairingLikeness.SamePath;
        }

        // One inside the other, so the paths below them differ by a prefix. Only
        // worth saying where the roots really are one place - two machines
        // reaching a share by different routes will not look like this, and two
        // unrelated folders should not be reported as a mistake.
        if (Inside(ours, them) || Inside(them, ours))
        {
            return PairingLikeness.FiledDifferently;
        }

        return string.Equals(Leaf(ours), Leaf(them), StringComparison.OrdinalIgnoreCase)
            ? PairingLikeness.SameName
            : null;
    }

    private static bool Inside(string parent, string child) =>
        child.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);

    private static string Trim(string root) =>
        root.Replace('/', '\\').TrimEnd('\\').Trim();

    private static string Leaf(string root) =>
        root.Split('\\', StringSplitOptions.RemoveEmptyEntries) is [.., string last]
            ? last
            : root;

    private static void Join(Dictionary<Guid, HashSet<Guid>> joined, Guid from, Guid to)
    {
        if (!joined.TryGetValue(from, out HashSet<Guid>? others))
        {
            joined[from] = others = [];
        }

        others.Add(to);
    }

    /// <summary>Everything reachable from one id, the link chain followed whole.</summary>
    private static HashSet<Guid> Reach(Dictionary<Guid, HashSet<Guid>> joined, Guid from)
    {
        HashSet<Guid> seen = [from];
        Queue<Guid> next = new([from]);

        while (next.Count > 0)
        {
            Guid here = next.Dequeue();

            if (!joined.TryGetValue(here, out HashSet<Guid>? others))
            {
                continue;
            }

            foreach (Guid other in others.Where(seen.Add))
            {
                next.Enqueue(other);
            }
        }

        return seen;
    }
}
