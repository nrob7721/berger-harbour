using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController : ControllerBase
{
    private readonly StaffBookingService service;

    public BookingsController(StaffBookingService service)
    {
        this.service = service;
    }

    /// <summary>Timeline data.</summary>
    [HttpGet]
    public Task<TimelineResponse> Timeline([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] bool includeInactive, CancellationToken ct) =>
        service.GetTimelineAsync(from, to, includeInactive, ct);

    [HttpGet("search")]
    public Task<IReadOnlyList<BookingSearchResult>> Search([FromQuery] string? q, CancellationToken ct) => service.SearchAsync(q, ct);

    [HttpGet("{id}")]
    public Task<AdminBookingDetail> Get(string id, CancellationToken ct) => service.GetAsync(id, ct);

    [HttpPost]
    public Task<BookingSaveResponse> Create(CreateStaffBookingRequest request, CancellationToken ct) => service.CreateAsync(request, ct);

    [HttpPut("{id}")]
    public Task<BookingSaveResponse> Update(string id, UpdateStaffBookingRequest request, CancellationToken ct) => service.UpdateAsync(id, request, ct);

    /// <summary>Rate-table prices for pre-filling and Recalculate from rates. Saves nothing.</summary>
    [HttpPost("quote")]
    public Task<AdminQuoteResponse> Quote(AdminQuoteRequest request, CancellationToken ct) => service.QuoteAsync(request, ct);

    [HttpPost("{id}/payments")]
    public Task<AdminBookingDetail> AddPayment(string id, ManualPaymentRequest request, CancellationToken ct) =>
        service.AddManualPaymentAsync(id, request, ct);

    [HttpPost("{id}/send-payment-link")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SendPaymentLink(string id, CancellationToken ct)
    {
        await service.SendPaymentLinkAsync(id, ct);
        return NoContent();
    }
}
