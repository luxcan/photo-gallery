namespace PhotoGallery.Application.Ports;

/// <summary>Whether answers can be exchanged yet, and what to say when not.</summary>
/// <param name="Problem">
/// Plain language, ready to show. Empty when there is nothing wrong. The screen
/// has to be able to say why rather than showing an empty list, because every
/// reason this fails is one the user can do something about.
/// </param>
public sealed record ExchangeReadiness(bool CanExchange, string Problem)
{
    public static ExchangeReadiness Ready { get; } = new(true, string.Empty);

    public static ExchangeReadiness Not(string problem) => new(false, problem);
}
