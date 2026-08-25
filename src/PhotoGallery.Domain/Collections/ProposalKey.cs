namespace PhotoGallery.Domain.Collections;

/// <summary>
/// What a proposal is called when the row it produced no longer exists.
/// </summary>
/// <remarks>
/// The load-bearing decision in this feature, and the one place the People
/// pattern does not transfer. A rejected face is remembered against a face row
/// and a person row, both of which are permanent. A proposed collection is a
/// derived cluster: the pass deletes and reinserts it, so its
/// <c>Id</c> changes for reasons that have nothing to do with the user. A
/// rejection keyed on that id would mean "dismiss this, rescan, and watch it
/// come back with a new number".
///
/// <para>The span of days is the cluster's own identity - a collection is
/// defined as a run of sessions on consecutive days, and runs cannot overlap -
/// so it is unique by construction, survives a rebuild, stays stable under the
/// change that actually happens (more photographs of the same days), and can be
/// read in the database by a person. A hash of the member set would have none
/// of those properties: one photograph more and the memory is lost.</para>
/// </remarks>
public static class ProposalKey
{
    /// <summary>The key for a run of days, first and last inclusive.</summary>
    public static string Of(DateOnly firstDay, DateOnly lastDay) =>
        $"{firstDay:yyyy-MM-dd}..{lastDay:yyyy-MM-dd}";

    /// <summary>The same, taken from the ends of a span.</summary>
    public static string Of(DateTime startUtc, DateTime endUtc) =>
        Of(DateOnly.FromDateTime(startUtc), DateOnly.FromDateTime(endUtc));
}
