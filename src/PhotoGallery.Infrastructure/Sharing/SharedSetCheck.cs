using PhotoGallery.Domain.Sharing;

namespace PhotoGallery.Infrastructure.Sharing;

/// <summary>
/// Whether a set read out of the sync folder has everything this build will
/// later take for granted.
/// </summary>
/// <remarks>
/// The JSON reader does not hold a file to the types' promises. A missing list
/// arrives as null, a missing key as a key with no path, a missing vector as one
/// with no values - and the file still reads cleanly. The null then surfaces
/// far from the file, in the merge or the take, as an error nothing on the way
/// up expects, and the app closes on every scan and every Sync until somebody
/// finds the file. A planted <c>{}</c> was enough.
///
/// <para>So every set is checked here, straight after it is read and inside
/// the same guard as a file caught half written, and a set with a hole in it is
/// that one file not read. Checked rather than read with the reader's own
/// strict options, because those would also refuse a member an older build
/// never wrote, and they say nothing about a key or a vector.</para>
///
/// <para>A name is held to the length the library keeps it at, too. SQLite does
/// not enforce a column's length, so a name of millions of characters would be
/// written in by the merge and drawn on every screen that shows it, long after
/// the file that brought it was gone.</para>
/// </remarks>
internal static class SharedSetCheck
{
    /// <summary>The longest name the library keeps: a person, an album, a shelf, a computer.</summary>
    private const int NameLength = 128;

    /// <summary>
    /// Far longer than any version, album key or model digest this app writes,
    /// all of which are a few dozen characters.
    /// </summary>
    private const int TagLength = 256;

    /// <summary>The longest path Windows can open.</summary>
    private const int PathLength = 32_767;

    /// <exception cref="InvalidDataException">Something this build relies on is missing.</exception>
    public static DecisionSet Whole(DecisionSet set)
    {
        Machine(set.Machine);

        Each(set.Sources, "the folders", source => Fits(source.Root, PathLength));
        Each(set.People, "the people", person => Fits(person.DisplayName, NameLength));
        Each(set.Answers, "the named faces", answer => Named(answer.Face));
        Each(set.Strangers, "the faces marked as nobody", stranger => Named(stranger.Face));
        Each(set.Turns, "the turned photographs", turn => Named(turn.Photo));
        Each(set.Albums, "the albums", Whole);
        Each(set.DeletedSuggestions, "the albums", Whole);
        Each(set.Memberships, "what is in each album", membership => Named(membership.Photo));
        Each(
            set.Rejections,
            "what was taken out of albums",
            rejection => Named(rejection.Photo) && Fits(rejection.ProposalKey, TagLength));
        Each(set.Eras, "the faces over the years", era => !era.Centroid.IsEmpty);
        Each(set.Links, "the folders paired up", _ => true);
        Each(set.Dismissals, "the albums turned down", dismissal => Fits(dismissal.ProposalKey, TagLength));
        Each(set.Collections, "the shelves", collection => Fits(collection.Name, NameLength));

        return set;
    }

    /// <exception cref="InvalidDataException">Something this build relies on is missing.</exception>
    public static PreparedSet Whole(PreparedSet set)
    {
        Machine(set.Machine);
        Models(set.Models);

        Each(
            set.Facts,
            "the prepared photographs",
            fact => Named(fact.Photo)
                && fact.Keyframes is not null
                && fact.Keyframes.All(frame => frame is not null));

        return set;
    }

    /// <exception cref="InvalidDataException">Something this build relies on is missing.</exception>
    public static FaceSet Whole(FaceSet set)
    {
        Machine(set.Machine);
        Models(set.Models);

        // An empty vector is the only wrong length that can arrive: one that is
        // present is held to 512 values by its converter, and one that is absent
        // reads as no values at all.
        Each(set.Faces, "the faces", face => Named(face.Face) && !face.Embedding.IsEmpty);

        return set;
    }

    private static void Machine(MachineIdentity machine)
    {
        // An empty id is not a machine: every file is named after the machine
        // that wrote it, and every answer forwarded is kept against it.
        if (machine is null
            || machine.Id == Guid.Empty
            || !Fits(machine.Name, NameLength)
            || !Fits(machine.AppVersion, TagLength))
        {
            throw Missing("which computer wrote it");
        }
    }

    private static void Models(IReadOnlyDictionary<string, string> models)
    {
        if (models is null
            || models.Any(model => !Fits(model.Key, TagLength) || !Fits(model.Value, TagLength)))
        {
            throw Missing("the models it ran");
        }
    }

    private static bool Whole(SharedAlbum album) =>
        Fits(album.Name, NameLength)
        && (album.ProposalKey is null || Fits(album.ProposalKey, TagLength))
        && (album.Cover is not AssetKey cover || Named(cover));

    private static bool Named(FaceKey face) => Named(face.Photo);

    /// <summary>A key the reader made up for a member the file did not have.</summary>
    private static bool Named(AssetKey photo) => Fits(photo.RelativePath, PathLength);

    private static bool Fits(string? text, int most) => text is not null && text.Length <= most;

    private static void Each<T>(IReadOnlyList<T> items, string what, Func<T, bool> whole)
        where T : class
    {
        if (items is null || items.Any(item => item is null || !whole(item)))
        {
            throw Missing(what);
        }
    }

    private static InvalidDataException Missing(string what) =>
        new($"Part of the file is missing or too long: {what}.");
}
