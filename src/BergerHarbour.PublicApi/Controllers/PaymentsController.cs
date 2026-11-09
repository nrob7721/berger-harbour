using BergerHarbour.Application.Payments;
using BergerHarbour.Application.Payments.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/payments/{token}")]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentLinkService service;

    public PaymentsController(PaymentLinkService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<PaymentLinkDetails> Get(string token, CancellationToken ct) => service.GetAsync(token, ct);

    [HttpPost("checkout")]
    public Task<CheckoutResponse> Checkout(string token, CancellationToken ct) => service.CheckoutAsync(token, ct);
}
