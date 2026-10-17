using System.Text.Json;
using PhotoGallery.Domain.Sharing;
using PhotoGallery.Infrastructure.Sharing;

namespace PhotoGallery.Infrastructure.Persistence;

/// <summary>
/// Held rows read back into the answers they hold.
/// </summary>
/// <remarks>
/// One reading for both of the places that need one: the sweep that lands
/// waiting answers, and a pairing that renames the folder inside each of them.
/// Two ways of reading a row would agree today and drift apart on the next kind
/// of answer somebody taught only one of them.
/// </remarks>
internal static class HeldRows
{
    /// <summary>The answers these rows hold, leaving out any that cannot be read.</summary>
    /// <remarks>
    /// A row that cannot be read is left where it is rather than thrown away or
    /// allowed to stop the caller. It was written by a version of this app, so
    /// the likely reasons are a newer one that wrote a shape this one does not
    /// know and a file somebody edited; in both cases the answer is worth more
    /// parked than deleted, and the other nine thousand should still land.
    /// </remarks>
    public static HeldAnswers Read(IEnumerable<HeldDecision> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        List<FaceAnswer> answers = [];
        List<StrangerFace> strangers = [];
        List<PhotoTurn> turns = [];
        List<SharedAlbumMembership> memberships = [];
        List<SharedAlbumRejection> rejections = [];

        foreach (HeldDecision row in rows)
        {
            switch (row.Kind)
            {
                case HeldDecisionKind.FaceAnswer:
                    Add(answers, row.Payload);
                    break;

                case HeldDecisionKind.Stranger:
                    Add(strangers, row.Payload);
                    break;

                case HeldDecisionKind.Turn:
                    Add(turns, row.Payload);
                    break;

                case HeldDecisionKind.SharedAlbumMembership:
                    Add(memberships, row.Payload);
                    break;

                case HeldDecisionKind.SharedAlbumRejection:
                    Add(rejections, row.Payload);
                    break;
            }
        }

        return new HeldAnswers(answers, strangers, turns, memberships, rejections);

        static void Add<T>(List<T> into, string payload)
        {
            try
            {
                if (JsonSerializer.Deserialize<T>(payload, DecisionSetFile.Shape) is T answer)
                {
                    into.Add(answer);
                }
            }
            catch (JsonException)
            {
            }
        }
    }
}
