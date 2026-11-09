namespace BergerHarbour.Application.Abstractions;

/// <summary>A created checkout session; the client secret mounts Embedded Checkout.</summary>
public sealed record CheckoutSession(string SessionId, string ClientSecret);
