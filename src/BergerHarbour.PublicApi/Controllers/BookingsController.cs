using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController : ControllerBase
{
    private readonly OnlineBookingService service;

    public BookingsController(OnlineBookingService service)
    {
        this.service = service;
    }

    /// <summary>Creates the PendingPayment hold and the deposit's Embedded Checkout session.</summary>
    [HttpPost]
    public Task<CreateBookingResponse> Create(CreateBookingRequest request, CancellationToken ct) => service.CreateAsync(request, ct);

    /// <summary>Back from the Payment step.</summary>
    [HttpPost("{bookingId}/release")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Release(string bookingId, ReleaseBookingRequest request, CancellationToken ct)
    {
        await service.ReleaseAsync(bookingId, request, ct);
        return NoContent();
    }

    [HttpGet("{bookingId}/status")]
    public Task<BookingStatusResponse> Status(string bookingId, [FromQuery(Name = "session_id")] string? sessionId, CancellationToken ct) =>
        service.GetStatusAsync(bookingId, sessionId, ct);
}
