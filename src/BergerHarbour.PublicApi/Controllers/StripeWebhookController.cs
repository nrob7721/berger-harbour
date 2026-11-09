using BergerHarbour.Application.Payments;
using BergerHarbour.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

/// <summary>Stripe-signature verified; exempt from the proxy secret and the rate limiter.</summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/stripe/webhook")]
public sealed class StripeWebhookController : ControllerBase
{
    private readonly StripeWebhookService service;

    public StripeWebhookController(StripeWebhookService service)
    {
        this.service = service;
    }

    /// <summary>Returns 2xx only after persistence succeeds, so Stripe retries on failure.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        await service.HandleAsync(await reader.ReadToEndAsync(ct), Request.Headers[Headers.StripeSignature].FirstOrDefault(), ct);
        return Ok();
    }
}
