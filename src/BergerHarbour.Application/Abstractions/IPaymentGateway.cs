using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Abstractions;

public enum CheckoutPurpose
{
    /// <summary>The checkout taken when a customer books online.</summary>
    Deposit,

    /// <summary>A later payment through a payment link.</summary>
    Payment,
}

public sealed record CheckoutSessionRequest(
    BookingId BookingId,
    string BookingReference,
    CheckoutPurpose Purpose,
    decimal Amount,
    string Description,
    string CustomerEmail,
    DateTimeOffset ExpiresAt);

public sealed record CheckoutSessionResult(string SessionId, string ClientSecret);

/// <summary>Creates Stripe Embedded Checkout sessions (only synchronous methods: card and wallets).</summary>
public interface IPaymentGateway
{
    Task<CheckoutSessionResult> CreateEmbeddedCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct = default);
}

/// <summary>A checkout session was paid. Amounts are AUD.</summary>
public sealed record CheckoutSessionCompleted(
    string EventId,
    string SessionId,
    string BookingId,
    CheckoutPurpose Purpose,
    string PaymentIntentId,
    decimal AmountPaid,
    DateTimeOffset PaidAt);

public sealed record CheckoutSessionExpired(string EventId, string SessionId, string BookingId);
