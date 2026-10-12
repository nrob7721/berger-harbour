using BergerHarbour.Application.Bookings;
using BergerHarbour.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

/// <summary>
/// The details a customer has entered before paying. Never a customer record: abandoned drafts are reported to staff.
/// </summary>
[ApiController]
[Route("api/booking-drafts")]
public sealed class BookingDraftsController(OnlineBookingService bookings) : ControllerBase
{
    /// <summary>Verifies Turnstile and starts a draft from "Your details".</summary>
    [HttpPost]
    public Task<BookingDraftCreatedDto> Create(CreateBookingDraftRequest request, CancellationToken ct) =>
        bookings.CreateDraftAsync(request, HttpContext.ClientIp(), ct);

    /// <summary>Records progress. 404 when the draft is unknown, converted or deleted.</summary>
    [HttpPut("{draftId}")]
    public async Task<IActionResult> Update(string draftId, UpdateBookingDraftRequest request, CancellationToken ct)
    {
        await bookings.UpdateDraftAsync(draftId, request, ct);
        return NoContent();
    }
}
