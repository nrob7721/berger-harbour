using BergerHarbour.Application.Abstractions;

namespace BergerHarbour.Application.Tests.Fakes;

/// <summary>Records sessions; sessions can be marked complete to simulate a payment finishing before a release.</summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public List<CheckoutSessionRequest> Created { get; } = [];

    public HashSet<string> Completed { get; } = [];

    public HashSet<string> Expired { get; } = [];

    public PaymentWebhookEvent? NextEvent { get; set; }

    public Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct = default)
    {
        Created.Add(request);
        var id = $"cs_test_{Created.Count}";
        return Task.FromResult(new CheckoutSession(id, id + "_secret"));
    }

    public Task<bool> TryExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (Completed.Contains(sessionId))
        {
            return Task.FromResult(false);
        }

        Expired.Add(sessionId);
        return Task.FromResult(true);
    }

    public PaymentWebhookEvent? ParseWebhookEvent(string payload, string? signature) =>
        signature == "valid" ? NextEvent : throw new InvalidWebhookException("Bad signature.");
}
