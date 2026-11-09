using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/addons/{addonId}/availability")]
public sealed class AddonAvailabilityController : ControllerBase
{
    private readonly AddonAvailabilityService service;

    public AddonAvailabilityController(AddonAvailabilityService service)
    {
        this.service = service;
    }

    /// <summary>Remaining capacity across all boats, excluding the booking being edited.</summary>
    [HttpGet]
    public Task<AddonRemainingResponse> Remaining(
        string addonId, [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, [FromQuery] string? excludeBookingId, CancellationToken ct) =>
        service.GetRemainingAsync(addonId, startDate, endDate, excludeBookingId, ct);
}
