using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Infrastructure.Stripe;
using BergerHarbour.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/booking-drafts")]
public sealed class BookingDraftsController(OnlineBookingService booking) : ControllerBase
{
    /// <summary>Verifies Turnstile and stores the customer's details as a booking draft. Never creates a customer.</summary>
    [HttpPost]
    public Task<CreateDraftResponse> Create(CreateDraftRequest request, CancellationToken ct) =>
        booking.CreateDraftAsync(request, Headers.ClientIp(HttpContext), ct);

    [HttpPut("{draftId}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Update(string draftId, UpdateDraftRequest request, CancellationToken ct)
    {
        await booking.UpdateDraftAsync(draftId, request, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(OnlineBookingService booking) : ControllerBase
{
    /// <summary>Creates a PendingPayment hold and a Stripe Embedded Checkout session for the deposit.</summary>
    [HttpPost]
    public Task<CreateBookingResponse> Create(CreateBookingRequest request, CancellationToken ct) => booking.CreateBookingAsync(request, ct);

    /// <summary>Back from the Payment step: frees the hold. Idempotent.</summary>
    [HttpPost("{bookingId}/release")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Release(string bookingId, ReleaseBookingRequest request, CancellationToken ct)
    {
        await booking.ReleaseAsync(bookingId, request.DraftId, ct);
        return NoContent();
    }

    [HttpGet("{bookingId}/status")]
    public Task<BookingStatusResponse> Status(string bookingId, [FromQuery(Name = "session_id")] string? sessionId, CancellationToken ct) =>
        booking.GetStatusAsync(bookingId, sessionId, ct);
}

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(PaymentLinkService payments) : ControllerBase
{
    [HttpGet("{token}")]
    public Task<PaymentLinkDetails> Get(string token, CancellationToken ct) => payments.GetAsync(token, ct);

    [HttpPost("{token}/checkout")]
    public Task<CheckoutResponse> Checkout(string token, CancellationToken ct) => payments.CreateCheckoutAsync(token, ct);
}

[ApiController]
[Route("api/stripe/webhook")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class StripeWebhookController(StripeWebhookParser parser, StripeWebhookService webhooks, ILogger<StripeWebhookController> logger) : ControllerBase
{
    /// <summary>Signature-verified. Returns 2xx only after persistence succeeds, so Stripe retries on failure.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);
        object? message;
        try
        {
            message = parser.Parse(json, Request.Headers["Stripe-Signature"].FirstOrDefault());
        }
        catch (Exception e) // a bad signature or an unreadable body: nothing was persisted
        {
            logger.LogWarning("Rejected a Stripe webhook: {Type} {Message}", e.GetType().Name, e.Message);
            return BadRequest();
        }
        try
        {
            switch (message)
            {
                case CheckoutCompleted completed: await webhooks.HandleCompletedAsync(completed, ct); break;
                case CheckoutExpired expired: await webhooks.HandleExpiredAsync(expired, ct); break;
            }
        }
        catch (Exception e)
        {
            logger.LogError(LogEvents.WebhookFailed, e, "WEBHOOK_FAILED: processing a Stripe webhook failed; Stripe will retry");
            throw;
        }
        return Ok();
    }
}
