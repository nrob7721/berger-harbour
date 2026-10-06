using BergerHarbour.Application.Abstractions;
using BergerHarbour.Infrastructure.Firestore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace BergerHarbour.Infrastructure.Stripe;

/// <summary>
/// Creates Stripe Embedded Checkout sessions (ui_mode embedded_page, because the booking app runs in an iframe and
/// hosted Checkout cannot be framed). Only card payments (including Apple Pay / Google Pay wallets) are allowed, so
/// checkout.session.completed always means paid. redirect_on_completion is "never": the app shows its own
/// completion state through Stripe.js onComplete.
/// </summary>
public sealed class StripePaymentGateway(IOptions<StripeOptions> options) : IPaymentGateway
{
    private readonly Lazy<StripeClient> _client = new(() => string.IsNullOrEmpty(options.Value.SecretKey)
        ? throw new InvalidOperationException("STRIPE_SECRET_KEY is not configured.")
        : new StripeClient(options.Value.SecretKey));

    public async Task<CheckoutSessionResult> CreateEmbeddedCheckoutSessionAsync(CheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, string>
        {
            ["bookingId"] = request.BookingId.Value,
            ["bookingReference"] = request.BookingReference,
            ["purpose"] = request.Purpose.ToString(),
        };
        var session = await _client.Value.V1.Checkout.Sessions.CreateAsync(new SessionCreateOptions
        {
            UiMode = "embedded_page",
            Mode = "payment",
            RedirectOnCompletion = "never",
            AllowedPaymentMethodTypes = ["card"],
            CustomerEmail = request.CustomerEmail,
            ClientReferenceId = request.BookingReference,
            ExpiresAt = request.ExpiresAt.UtcDateTime,
            Locale = "en-AU",
            SubmitType = "pay",
            Metadata = metadata,
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                Description = request.Description,
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
                        UnitAmount = FirestoreMapping.Cents(request.Amount),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = request.Purpose == CheckoutPurpose.Deposit
                                ? $"Houseboat booking {request.BookingReference}"
                                : $"Payment for booking {request.BookingReference}",
                            Description = request.Description,
                        },
                    },
                },
            ],
        }, new RequestOptions { IdempotencyKey = $"{request.BookingId.Value}-{request.Purpose}-{Guid.NewGuid():N}" }, ct);

        return new CheckoutSessionResult(session.Id, session.ClientSecret);
    }
}
