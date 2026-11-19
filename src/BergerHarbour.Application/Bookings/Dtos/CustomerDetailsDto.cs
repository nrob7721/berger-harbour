using BergerHarbour.Application.Customers.Dtos;

namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>
/// An online booking's contact details snapshot: what the customer entered online, and, while it is Expired, the staff's
/// edits including the address and customer comments (§7.3).
/// </summary>
public sealed record CustomerDetailsDto(
    string FirstName,
    string LastName,
    string Email,
    string MobileNumber,
    AddressDto? Address,
    string? CustomerComments);
