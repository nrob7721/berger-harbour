using BergerHarbour.Application.Common;
using BergerHarbour.Application.Customers;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

/// <summary>Read-only in v1. Contact details are updated from the booking dialog.</summary>
[ApiController]
[Route("api/customers")]
public sealed class CustomersController(CustomerQueries customers) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CustomerDto>> List([FromQuery] string? q, CancellationToken ct) => customers.ListAsync(q, ct);

    /// <summary>Used by the booking dialog to pre-fill an existing customer.</summary>
    [HttpGet("by-email")]
    public async Task<CustomerDto> ByEmail([FromQuery] string email, CancellationToken ct) =>
        await customers.GetByEmailAsync(email, ct) ?? throw new NotFoundException("No customer with that email.");
}
