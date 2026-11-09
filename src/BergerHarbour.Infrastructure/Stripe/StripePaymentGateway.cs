using BergerHarbour.Application.Abstractions;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace BergerHarbour.Infrastructure.Stripe;

public sealed class StripeOptions
{
    public string SecretKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}

/// <summary>Stripe Embedded Checkout (the booking app runs inside an iframe, which hosted Checkout can't).</summary>
public sealed class StripePaymentGateway(IOptions<StripeOptions> options, ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
    private SessionService Sessions()
    {
        if (string.IsNullOrWhiteSpace(options.Value.SecretKey))
            throw new InvalidOperationException("Stripe is not configured (STRIPE_SECRET_KEY).");
        return new SessionService(new StripeClient(options.Value.SecretKey));
    }

    public async Task<CheckoutSession> CreateEmbeddedCheckoutAsync(CheckoutRequest r, CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, string> { ["bookingId"] = r.BookingId, ["purpose"] = r.Purpose.ToString() };
        if (r.Reference is not null) metadata["reference"] = r.Reference;
        var create = new SessionCreateOptions
        {
            UiMode = "embedded_page",
            Mode = "payment",
            RedirectOnCompletion = "if_required",
            ReturnUrl = r.ReturnUrl,
            // Only synchronous methods (cards, and wallets through them), so checkout.session.completed means paid.
            AllowedPaymentMethodTypes = ["card"],
            Locale = "auto",
            CustomerEmail = r.CustomerEmail,
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Description = r.Reference is null ? r.Description : $"{r.Reference} – {r.Description}",
                Metadata = metadata,
            },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "aud",
                        UnitAmount = Money.ToCents(r.Amount),
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = r.Description },
                    },
                },
            ],
            ExpiresAt = r.ExpiresAt?.UtcDateTime,
        };
        var session = await Sessions().CreateAsync(create, cancellationToken: ct);
        return new CheckoutSession(session.Id, session.ClientSecret);
    }

    public async Task<ExpireSessionResult> ExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var service = Sessions();
        var session = await service.GetAsync(sessionId, cancellationToken: ct);
        switch (session.Status)
        {
            case "complete": return ExpireSessionResult.AlreadyCompleted;
            case "expired": return ExpireSessionResult.AlreadyExpired;
        }
        try
        {
            await service.ExpireAsync(sessionId, cancellationToken: ct);
            return ExpireSessionResult.Expired;
        }
        catch (StripeException e)
        {
            logger.LogWarning(e, "Expiring Checkout session {SessionId} failed; checking its status", sessionId);
            var latest = await service.GetAsync(sessionId, cancellationToken: ct);
            return latest.Status == "complete" ? ExpireSessionResult.AlreadyCompleted : ExpireSessionResult.AlreadyExpired;
        }
    }
}

/// <summary>Verifies the Stripe signature and turns events into application messages.</summary>
public sealed class StripeWebhookParser(IOptions<StripeOptions> options)
{
    /// <summary>Null for event types this system ignores. Throws <see cref="StripeException"/> for a bad signature.</summary>
    public object? Parse(string json, string? signature)
    {
        var e = EventUtility.ConstructEvent(json, signature, options.Value.WebhookSecret, throwOnApiVersionMismatch: false);
        if (e.Data.Object is not Session s || !s.Metadata.TryGetValue("bookingId", out var bookingId)) return null;
        var purpose = s.Metadata.TryGetValue("purpose", out var p) && Enum.TryParse<CheckoutPurpose>(p, out var parsed) ? parsed : CheckoutPurpose.Payment;
        return e.Type switch
        {
            EventTypes.CheckoutSessionCompleted when s.PaymentStatus == "paid" => new CheckoutCompleted(
                s.Id, bookingId, purpose, s.PaymentIntentId ?? s.Id, Money.FromCents(s.AmountTotal ?? 0), new DateTimeOffset(e.Created, TimeSpan.Zero)),
            EventTypes.CheckoutSessionExpired => new CheckoutExpired(s.Id, bookingId),
            _ => null,
        };
    }
}
