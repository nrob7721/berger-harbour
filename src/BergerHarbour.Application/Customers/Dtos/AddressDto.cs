namespace BergerHarbour.Application.Customers.Dtos;

/// <summary>A customer's address in parts (§7.3). Every part is optional; <c>null</c> for the whole address means none.</summary>
public sealed record AddressDto(
    string? Line1,
    string? Line2,
    string? Locality,
    string? State,
    string? Postcode,
    string? Country);
