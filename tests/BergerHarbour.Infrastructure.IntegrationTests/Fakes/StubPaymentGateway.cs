using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Infrastructure.IntegrationTests.Fakes;

public sealed class StubPaymentGateway : IPaymentGateway
{
    public Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CheckoutSession("cs_" + Guid.NewGuid().ToString("N"), "secret"));

    public Task<bool> TryExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);

    public PaymentWebhookEvent? ParseWebhookEvent(string payload, string? signature) => throw new InvalidWebhookException("Not in tests.");
}
