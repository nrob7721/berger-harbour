using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Bookings.Dtos;
using BergerHarbour.Domain.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.PublicApi.Controllers;

[ApiController]
[Route("api/availability")]
public sealed class AvailabilityController : ControllerBase
{
    private readonly AvailabilityService service;

    public AvailabilityController(AvailabilityService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<FleetAvailabilityResponse> Fleet([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        service.GetFleetAsync(from, to, ct);

    [HttpGet("period")]
    public Task<PeriodAvailabilityResponse> Period([FromQuery] PeriodType? periodType, [FromQuery] DateOnly? startDate, CancellationToken ct) =>
        service.GetPeriodAsync(periodType, startDate, ct);
}
