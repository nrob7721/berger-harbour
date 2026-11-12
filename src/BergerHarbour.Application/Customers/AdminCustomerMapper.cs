using BergerHarbour.Application.Customers.Dtos;
using BergerHarbour.Domain.Customers;

namespace BergerHarbour.Application.Customers;

/// <summary>Customer → <see cref="AdminCustomer"/>, and addresses to and from <see cref="AddressDto"/>.</summary>
public static class AdminCustomerMapper
{
    public static AdminCustomer ToDto(this Customer c) =>
        new(c.Id.Value, c.FirstName, c.LastName, c.Email.Value, c.MobileNumber?.Value, c.SecondaryMobileNumber?.Value, c.Address.ToDto(), c.Comments,
            c.CreatedDate, c.Version);

    public static AddressDto? ToDto(this Address? a) =>
        a is null ? null : new(a.Line1, a.Line2, a.Locality, a.State, a.Postcode, a.Country);

    /// <summary>Validates the parts; throws a field error per invalid part.</summary>
    public static Address? ToAddress(this AddressDto? dto) =>
        dto is null ? null : Address.Create(dto.Line1, dto.Line2, dto.Locality, dto.State, dto.Postcode, dto.Country);
}
