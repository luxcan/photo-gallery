namespace PhotoGallery.Domain.Sharing.Direct;

/// <summary>Why the other computers cannot be found on the network.</summary>
/// <remarks>
/// Every one of these has to be said on screen rather than discovered. An empty
/// list is the same picture whether nobody is running the app, the firewall is
/// closed, or the network will never carry the packet at all - and the three
/// need completely different things from the person looking at it.
/// </remarks>
public enum DiscoveryProblem
{
    /// <summary>Nothing is wrong; the other machines simply have not answered.</summary>
    None = 0,

    /// <summary>
    /// The network is set to Public, which blocks inbound traffic outright.
    /// </summary>
    /// <remarks>
    /// What Windows chooses by default, and what a great many home networks are
    /// left as. Discovery finds nothing and there is no error to report, so the
    /// screen has to say this rather than show an empty list.
    /// </remarks>
    PublicNetwork = 1,

    /// <summary>
    /// No network this machine can multicast on at all.
    /// </summary>
    /// <remarks>
    /// A laptop on nothing but a mobile connection, or with every adapter down.
    /// </remarks>
    NoNetwork = 2,

    /// <summary>
    /// The socket could not be opened, which on Windows means the firewall
    /// prompt was refused.
    /// </summary>
    Blocked = 3,
}

/// <summary>What to say about a discovery problem, and what to offer instead.</summary>
/// <remarks>
/// Every one of these ends the same way: there is a typed address as the way
/// through and the shared folder as the way round. A screen that reported the
/// problem and stopped would be a screen that had explained why the user cannot
/// do the thing they came to do.
/// </remarks>
public static class DiscoveryProblems
{
    public static string Explain(this DiscoveryProblem problem) => problem switch
    {
        DiscoveryProblem.PublicNetwork =>
            "This computer's network is set to Public, which stops other computers reaching "
          + "it. Windows chooses Public by default. You can change it in Network settings - "
          + "or type the other computer's address below instead.",

        DiscoveryProblem.NoNetwork =>
            "This computer is not on a network, so there is nobody to find. Type the other "
          + "computer's address below if you know it.",

        DiscoveryProblem.Blocked =>
            "Windows is blocking Photo Gallery from listening on the network. Allow it when "
          + "Windows asks - or type the other computer's address below instead.",

        _ => string.Empty,
    };

    /// <summary>Whether anything needs saying at all.</summary>
    public static bool NeedsSaying(this DiscoveryProblem problem) =>
        problem != DiscoveryProblem.None;
}
