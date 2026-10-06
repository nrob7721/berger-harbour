using BergerHarbour.Application.Bookings;
using BergerHarbour.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(OnlineBookingService bookings) : ControllerBase
{
    /// <summary>Creates a PendingPayment hold and an Embedded Checkout session for the amount payable now.</summary>
    [HttpPost]
    public Task<CreateOnlineBookingResult> Create(CreateOnlineBookingRequest request, CancellationToken ct) =>
        bookings.CreateAsync(request, HttpContext.ClientIp(), ct);

    /// <summary>Completion screen polling. Only answers when session_id matches the booking's deposit checkout.</summary>
    [HttpGet("{reference}/status")]
    public Task<BookingStatusDto> Status(string reference, [FromQuery(Name = "session_id")] string? sessionId,
        CancellationToken ct) => bookings.GetStatusAsync(reference, sessionId, ct);
}
