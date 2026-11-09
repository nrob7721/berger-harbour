using BergerHarbour.Application.Boats;
using BergerHarbour.Application.Bookings;
using BergerHarbour.Application.Customers;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/boats")]
public sealed class BoatsController(FleetService fleet) : ControllerBase
{
    /// <summary>All boats, berth desc then name.</summary>
    [HttpGet]
    public Task<IReadOnlyList<AdminBoat>> List(CancellationToken ct) => fleet.ListAsync(ct);

    [HttpGet("{id}")]
    public Task<AdminBoat> Get(string id, CancellationToken ct) => fleet.GetAsync(id, ct);

    [HttpPost]
    public Task<AdminBoat> Create(SaveBoatRequest request, CancellationToken ct) => fleet.CreateAsync(request, ct);

    [HttpPut("{id}")]
    public Task<AdminBoat> Update(string id, SaveBoatRequest request, CancellationToken ct) => fleet.UpdateAsync(id, request, ct);

    [HttpGet("{id}/unavailabilities")]
    public Task<IReadOnlyList<UnavailabilityDto>> Unavailabilities(string id, CancellationToken ct) => fleet.ListUnavailabilitiesAsync(id, ct);

    [HttpPost("{id}/unavailabilities")]
    public Task<UnavailabilityDto> CreateUnavailability(string id, SaveUnavailabilityRequest request, CancellationToken ct) =>
        fleet.CreateUnavailabilityAsync(id, request, ct);

    [HttpPut("{id}/unavailabilities/{uid}")]
    public Task<UnavailabilityDto> UpdateUnavailability(string id, string uid, SaveUnavailabilityRequest request, CancellationToken ct) =>
        fleet.UpdateUnavailabilityAsync(id, uid, request, ct);

    [HttpDelete("{id}/unavailabilities/{uid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteUnavailability(string id, string uid, CancellationToken ct)
    {
        await fleet.DeleteUnavailabilityAsync(id, uid, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/customers")]
public sealed class CustomersController(CustomerAdminService customers) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AdminCustomer>> List([FromQuery] string? q, CancellationToken ct) => customers.ListAsync(q, ct);

    /// <summary>Pre-fills the booking dialog; includes the Frequent Floater check anchored at now.</summary>
    [HttpGet("by-email")]
    public Task<CustomerLookupResponse> ByEmail([FromQuery] string email, CancellationToken ct) => customers.GetByEmailAsync(email, ct);

    [HttpPut("{id}/comments")]
    public Task<AdminCustomer> UpdateComments(string id, UpdateCustomerCommentsRequest request, CancellationToken ct) =>
        customers.UpdateCommentsAsync(id, request, ct);

    /// <summary>All of the customer's bookings, every status, newest stay first.</summary>
    [HttpGet("{id}/bookings")]
    public Task<IReadOnlyList<CustomerBookingItem>> Bookings(string id, CancellationToken ct) => customers.ListBookingsAsync(id, ct);
}
