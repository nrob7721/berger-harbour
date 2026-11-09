using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/addons/{addonId}/availability")]
public sealed class AddonAvailabilityController : ControllerBase
{
    private readonly AddonAvailabilityService service;

    public AddonAvailabilityController(AddonAvailabilityService service)
    {
        this.service = service;
    }

    /// <summary>Remaining capacity across all boats. Customers can't exclude a booking.</summary>
    [HttpGet]
    public Task<AddonRemainingResponse> Remaining(string addonId, [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken ct) =>
        service.GetRemainingAsync(addonId, startDate, endDate, null, ct);
}
