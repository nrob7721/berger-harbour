namespace BergerHarbour.Application.Payments.Dtos;

/// <summary>An Embedded Checkout session for the amount due now.</summary>
public sealed record CheckoutResponse(
    string ClientSecret,
    string SessionId);
