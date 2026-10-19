using BergerHarbour.Application.Bookings;
using BergerHarbour.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(AdminBookingService bookings) : ControllerBase
{
    /// <summary>Timeline data for the Bookings page.</summary>
    [HttpGet]
    public Task<TimelineDto> Timeline([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] bool includeInactive, CancellationToken ct) => bookings.GetTimelineAsync(from, to, includeInactive, ct);

    /// <summary>Search by customer name, email or booking reference.</summary>
    [HttpGet("search")]
    public Task<IReadOnlyList<BookingSearchResultDto>> Search([FromQuery] string? q, CancellationToken ct) =>
        bookings.SearchAsync(q, ct);

    /// <summary>Pre-fills the booking dialog's hire price and end date.</summary>
    [HttpGet("quote")]
    public Task<AdminQuoteDto> Quote([FromQuery] string boatId, [FromQuery] PeriodType periodType,
        [FromQuery] DateOnly startDate, CancellationToken ct) => bookings.QuoteAsync(boatId, periodType, startDate, ct);

    [HttpGet("{id}")]
    public Task<BookingDetailDto> Get(string id, CancellationToken ct) => bookings.GetAsync(id, ct);

    [HttpPost]
    public Task<BookingDetailDto> Create(AdminBookingRequest request, CancellationToken ct) => bookings.CreateAsync(request, ct);

    [HttpPut("{id}")]
    public Task<BookingDetailDto> Update(string id, AdminBookingUpdateRequest request, CancellationToken ct) =>
        bookings.UpdateAsync(id, request, ct);

    [HttpPost("{id}/recalculate-price")]
    public Task<BookingDetailDto> RecalculatePrice(string id, VersionRequest request, CancellationToken ct) =>
        bookings.RecalculatePriceAsync(id, request, ct);

    [HttpPost("{id}/addons")]
    public Task<BookingDetailDto> AddAddon(string id, AddAddonLineRequest request, CancellationToken ct) =>
        bookings.AddAddonAsync(id, request, ct);

    [HttpPut("{id}/addons/{lineId}")]
    public Task<BookingDetailDto> UpdateAddon(string id, string lineId, UpdateAddonLineRequest request, CancellationToken ct) =>
        bookings.UpdateAddonAsync(id, lineId, request, ct);

    [HttpDelete("{id}/addons/{lineId}")]
    public Task<BookingDetailDto> RemoveAddon(string id, string lineId, [FromQuery] int version, CancellationToken ct) =>
        bookings.RemoveAddonAsync(id, lineId, version, ct);

    /// <summary>Records a payment received outside Stripe.</summary>
    [HttpPost("{id}/payments")]
    public Task<BookingDetailDto> AddPayment(string id, ManualPaymentRequest request, CancellationToken ct) =>
        bookings.AddManualPaymentAsync(id, request, ct);

    /// <summary>Emails #4 now, for the amount due now.</summary>
    [HttpPost("{id}/send-payment-link")]
    public Task<SendPaymentLinkResult> SendPaymentLink(string id, CancellationToken ct) => bookings.SendPaymentLinkAsync(id, ct);
}
