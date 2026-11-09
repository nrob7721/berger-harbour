namespace BergerHarbour.Application.Abstractions;

/// <summary>Stripe Embedded Checkout behind a port, so Stripe types stay in Infrastructure.</summary>
public interface IPaymentGateway
{
    Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct = default);

    /// <summary>Expires an open session. Returns false when the session was already paid.</summary>
    Task<bool> TryExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Verifies the signature and parses the event. Returns null for event types the system ignores. Throws
    /// <see cref="InvalidWebhookException"/> when the signature or payload is invalid.
    /// </summary>
    PaymentWebhookEvent? ParseWebhookEvent(string payload, string? signature);
}
