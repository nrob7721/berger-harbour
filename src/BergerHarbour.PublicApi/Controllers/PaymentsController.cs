using BergerHarbour.Application.Payments;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(PaymentLinkService payments) : ControllerBase
{
    /// <summary>The pay page reached from emailed payment links.</summary>
    [HttpGet("{token}")]
    public Task<PayPageDto> Get(string token, CancellationToken ct) => payments.GetAsync(token, ct);

    /// <summary>Creates an Embedded Checkout session for the amount due now. 400 when nothing is owing.</summary>
    [HttpPost("{token}/checkout")]
    public Task<PayCheckoutResult> Checkout(string token, CancellationToken ct) => payments.CreateCheckoutAsync(token, ct);
}
