using System.Globalization;
using BergerHarbour.Application.Abstractions;
using BergerHarbour.Domain.Bookings;
using BergerHarbour.Domain.Shared;
using global::Stripe;
using global::Stripe.Checkout;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Stripe;

/// <summary>
/// Stripe Embedded Checkout (<c>ui_mode: embedded_page</c>), card only so <c>checkout.session.completed</c> means paid
/// (wallets come through the card method). Amounts are AUD including GST.
/// </summary>
public sealed class StripePaymentGateway : IPaymentGateway
{
    private const string BookingIdKey = "bookingId";
    private const string PurposeKey = "purpose";

    private readonly StripeOptions options;
    private readonly StripeClient? client;

    public StripePaymentGateway(IOptions<StripeOptions> options)
    {
        this.options = options.Value;
        client = this.options.IsConfigured ? new StripeClient(this.options.SecretKey) : null;
    }

    public async Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, string>
        {
            [BookingIdKey] = request.BookingId.Value,
            [PurposeKey] = request.Purpose.ToString(),
        };
        var create = new SessionCreateOptions
        {
            UiMode = "embedded_page",
            Mode = "payment",
            RedirectOnCompletion = "if_required",
            ReturnUrl = request.ReturnUrl,
            Locale = "auto",
            AllowedPaymentMethodTypes = ["card"],
            CustomerEmail = string.IsNullOrWhiteSpace(request.CustomerEmail) ? null : request.CustomerEmail,
            ExpiresAt = request.ExpiresAt?.UtcDateTime,
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions { Description = request.Description, Metadata = metadata },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "aud",
                        UnitAmount = (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero),
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = request.ProductName, Description = request.Description },
                    },
                },
            ],
        };
        var session = await new SessionService(Client).CreateAsync(create, cancellationToken: ct);
        return new CheckoutSession(session.Id, session.ClientSecret);
    }

    public async Task<bool> TryExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var service = new SessionService(Client);
        var session = await service.GetAsync(sessionId, cancellationToken: ct);
        switch (session.Status)
        {
            case "complete":
                return false;
            case "expired":
                return true;
        }

        try
        {
            await service.ExpireAsync(sessionId, cancellationToken: ct);
            return true;
        }
        catch (StripeException)
        {
            // Completed or expired in the meantime: decide from its current state.
            return (await service.GetAsync(sessionId, cancellationToken: ct)).Status != "complete";
        }
    }

    public PaymentWebhookEvent? ParseWebhookEvent(string payload, string? signature)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature ?? string.Empty, options.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (Exception e) when (e is StripeException or ArgumentException or FormatException or Newtonsoft.Json.JsonException)
        {
            throw new InvalidWebhookException("The Stripe signature or payload is invalid.", e);
        }

        if (stripeEvent.Data.Object is not Session session
            || !session.Metadata.TryGetValue(BookingIdKey, out var bookingId)
            || !session.Metadata.TryGetValue(PurposeKey, out var purposeText)
            || !Enum.TryParse<CheckoutPurpose>(purposeText, out var purpose))
        {
            return null;
        }

        return stripeEvent.Type switch
        {
            EventTypes.CheckoutSessionCompleted when session.PaymentStatus == "paid" =>
                new CheckoutSessionCompleted(session.Id, new BookingId(bookingId), purpose, session.PaymentIntentId ?? session.Id,
                    (session.AmountTotal ?? 0) / 100m, new DateTimeOffset(DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc))),
            EventTypes.CheckoutSessionExpired => new CheckoutSessionExpired(session.Id, new BookingId(bookingId)),
            _ => null,
        };
    }

    private StripeClient Client =>
        client ?? throw new BusinessRuleException("Online payments aren't configured (STRIPE_SECRET_KEY is empty).");

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"Stripe (configured: {options.IsConfigured})");
}
