using BergerHarbour.Application.Customers;
using BergerHarbour.Application.Customers.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace BergerHarbour.AdminApi.Controllers;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly CustomerService service;

    public CustomersController(CustomerService service)
    {
        this.service = service;
    }

    [HttpGet]
    public Task<IReadOnlyList<AdminCustomer>> List([FromQuery] string? q, CancellationToken ct) => service.ListAsync(q, ct);

    [HttpGet("by-email")]
    public Task<CustomerLookupResponse> ByEmail([FromQuery] string? email, CancellationToken ct) => service.GetByEmailAsync(email, ct);

    [HttpPut("{id}/comments")]
    public Task<AdminCustomer> UpdateComments(string id, UpdateCustomerCommentsRequest request, CancellationToken ct) =>
        service.UpdateCommentsAsync(id, request, ct);

    [HttpGet("{id}/bookings")]
    public Task<IReadOnlyList<CustomerBookingItem>> Bookings(string id, CancellationToken ct) => service.ListBookingsAsync(id, ct);
}
