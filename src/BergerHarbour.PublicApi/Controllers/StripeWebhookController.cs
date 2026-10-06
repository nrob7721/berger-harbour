using BergerHarbour.Application.Payments;
using BergerHarbour.Infrastructure.Stripe;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

/// <summary>
/// Stripe webhooks. Verified by the Stripe signature (exempt from the proxy secret). Returns 2xx only after the
/// change is persisted, so Stripe retries on failure.
/// </summary>
[ApiController]
[Route(Route)]
public sealed class StripeWebhookController(StripeWebhookParser parser, StripeWebhookHandler handler,
    ILogger<StripeWebhookController> logger) : ControllerBase
{
    public const string Route = "/api/stripe/webhook";

    [HttpPost]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(ct);
        var evt = parser.Parse(json, Request.Headers["Stripe-Signature"].FirstOrDefault());
        try
        {
            switch (evt)
            {
                case CompletedWebhookEvent completed:
                    await handler.HandleCompletedAsync(completed.Event, ct);
                    break;
                case ExpiredWebhookEvent expired:
                    await handler.HandleExpiredAsync(expired.Event, ct);
                    break;
                case IgnoredWebhookEvent ignored:
                    logger.LogInformation("Ignoring Stripe event {Type}", ignored.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WEBHOOK_PROCESSING_ERROR: Stripe webhook processing failed");
            throw;
        }

        return Ok();
    }
}
