namespace PhotoGallery.Domain.Sharing.Direct;

/// <summary>
/// What a machine says about itself on the family network, and nothing else.
/// </summary>
/// <remarks>
/// <strong>It carries nothing about the library.</strong> Not how many
/// photographs, not whose they are, not a folder name - a packet that leaves
/// this machine unasked and unencrypted says who is here and how to reach them,
/// and stops there. Everything else is said over a connection that has been
/// paired.
///
/// <para>The fingerprint is in the packet so that a machine already paired can
/// recognise a peer before connecting, and so that the check value binding a
/// pairing code to the channel can be computed before either side trusts the
/// other.</para>
/// </remarks>
public sealed record Beacon(
    Guid MachineId,
    string Name,
    string AppVersion,
    int SchemaVersion,
    int Port,
    string Fingerprint)
{
    /// <summary>
    /// The multicast group these are sent to.
    /// </summary>
    /// <remarks>
    /// The administratively-scoped local range, so it does not leave the house:
    /// a router will not forward it, which is the whole reason for choosing this
    /// block rather than a globally-scoped one.
    /// </remarks>
    public const string Group = "239.255.42.7";

    public const int GroupPort = 41871;

    /// <summary>Whether this is a beacon this machine should pay attention to.</summary>
    /// <remarks>
    /// Its own is not: a machine hears every packet it sends, and a laptop
    /// offering to pair with itself is the first thing anybody would notice.
    /// </remarks>
    public bool IsWorthAnswering(Guid mine) =>
        MachineId != Guid.Empty && MachineId != mine && Port is > 0 and <= 65535;
}
