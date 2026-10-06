using BergerHarbour.Application.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace BergerHarbour.Infrastructure.Stripe;

public abstract record StripeWebhookEvent;

public sealed record CompletedWebhookEvent(CheckoutSessionCompleted Event) : StripeWebhookEvent;

public sealed record ExpiredWebhookEvent(CheckoutSessionExpired Event) : StripeWebhookEvent;

public sealed record IgnoredWebhookEvent(string Type) : StripeWebhookEvent;

public sealed class InvalidStripeSignatureException(string message, Exception inner) : Exception(message, inner);

/// <summary>Verifies the Stripe-Signature header and turns checkout events into application events.</summary>
public sealed class StripeWebhookParser(IOptions<StripeOptions> options)
{
    public StripeWebhookEvent Parse(string json, string? signatureHeader)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, options.Value.WebhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            throw new InvalidStripeSignatureException("Invalid Stripe webhook signature.", ex);
        }

        return FromEvent(stripeEvent);
    }

    public static StripeWebhookEvent FromEvent(Event stripeEvent)
    {
        if (stripeEvent.Data.Object is not Session session)
        {
            return new IgnoredWebhookEvent(stripeEvent.Type);
        }

        var metadata = session.Metadata ?? new Dictionary<string, string>();
        if (!metadata.TryGetValue("bookingId", out var bookingId) || string.IsNullOrEmpty(bookingId))
        {
            return new IgnoredWebhookEvent(stripeEvent.Type);
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted when session.PaymentStatus == "paid":
                var purpose = metadata.TryGetValue("purpose", out var p) && Enum.TryParse<CheckoutPurpose>(p, out var parsed)
                    ? parsed
                    : CheckoutPurpose.Payment;
                return new CompletedWebhookEvent(new CheckoutSessionCompleted(stripeEvent.Id, session.Id, bookingId, purpose,
                    session.PaymentIntentId ?? session.Id, (session.AmountTotal ?? 0) / 100m,
                    new DateTimeOffset(DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc))));
            case EventTypes.CheckoutSessionExpired:
                return new ExpiredWebhookEvent(new CheckoutSessionExpired(stripeEvent.Id, session.Id, bookingId));
            default:
                return new IgnoredWebhookEvent(stripeEvent.Type);
        }
    }
}
