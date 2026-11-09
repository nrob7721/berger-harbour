using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Domain.Customers;

namespace BergerHarbour.Application.Customers;

/// <summary>Customer → <see cref="AdminCustomer"/>.</summary>
public static class AdminCustomerMapper
{
    public static AdminCustomer ToDto(this Customer c) =>
        new(c.Id.Value, c.FirstName, c.LastName, c.Email.Value, c.MobileNumber?.Value, c.SecondaryMobileNumber?.Value, c.Address, c.Comments,
            c.CreatedDate, c.Version);
}
