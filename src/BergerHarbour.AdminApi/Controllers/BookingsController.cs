using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Catalogue;
using BergerHarbour.Application.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(AdminBookingService bookings) : ControllerBase
{
    /// <summary>Timeline data: house boats (berth desc, then name), bookings, unavailabilities and blocked periods.</summary>
    [HttpGet]
    public Task<TimelineResponse> Timeline([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] bool includeInactive, CancellationToken ct) =>
        bookings.GetTimelineAsync(from, to, includeInactive, ct);

    /// <summary>By name, email or reference, including details customers entered online.</summary>
    [HttpGet("search")]
    public Task<IReadOnlyList<BookingSearchResult>> Search([FromQuery] string? q, CancellationToken ct) => bookings.SearchAsync(q, ct);

    [HttpGet("{id}")]
    public Task<AdminBookingDetail> Get(string id, CancellationToken ct) => bookings.GetAsync(id, ct);

    [HttpPost]
    public Task<BookingSaveResponse> Create(CreateStaffBookingRequest request, CancellationToken ct) => bookings.CreateAsync(request, ct);

    [HttpPut("{id}")]
    public Task<BookingSaveResponse> Update(string id, UpdateStaffBookingRequest request, CancellationToken ct) => bookings.UpdateAsync(id, request, ct);

    /// <summary>Prices for pre-filling and "Recalculate from rates". Saves nothing.</summary>
    [HttpPost("quote")]
    public Task<AdminQuoteResponse> Quote(AdminQuoteRequest request, CancellationToken ct) => bookings.QuoteAsync(request, ct);

    [HttpPost("{id}/payments")]
    public Task<AdminBookingDetail> AddPayment(string id, ManualPaymentRequest request, CancellationToken ct) =>
        bookings.AddManualPaymentAsync(id, request, ct);

    /// <summary>Sends the payment-due email now, for the amount due now.</summary>
    [HttpPost("{id}/send-payment-link")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> SendPaymentLink(string id, CancellationToken ct)
    {
        await bookings.SendPaymentLinkAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/addons")]
public sealed class AddonsController(PublicCatalogueService catalogue) : ControllerBase
{
    /// <summary>Remaining capacity for the dates, optionally excluding the booking being edited.</summary>
    [HttpGet("{addonId}/availability")]
    public Task<AddonRemainingResponse> Availability(string addonId, [FromQuery] DateOnly startDate, [FromQuery] DateOnly endDate,
        [FromQuery] string? excludeBookingId, CancellationToken ct) =>
        catalogue.GetAddonRemainingAsync(addonId, startDate, endDate, excludeBookingId, ct);
}

[ApiController]
[Route(JobsAuthentication.Path)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class JobsController(NotificationJob job, JobsAuthentication auth, ILogger<JobsController> logger) : ControllerBase
{
    /// <summary>Called hourly by Cloud Scheduler (OIDC). Idempotent.</summary>
    [HttpPost("process-notifications")]
    public async Task<IActionResult> ProcessNotifications(CancellationToken ct)
    {
        if (!await auth.IsAuthorisedAsync(HttpContext)) return Unauthorized();
        try
        {
            var result = await job.RunAsync(ct);
            return result.Failures > 0 ? StatusCode(500, result) : Ok(result);
        }
        catch (Exception e)
        {
            logger.LogError(LogEvents.JobFailed, e, "JOB_FAILED: process-notifications failed");
            throw;
        }
    }
}
