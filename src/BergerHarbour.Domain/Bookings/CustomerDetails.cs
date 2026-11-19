using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>
/// An online booking's contact details snapshot. The customer enters the name, email and mobile online; staff edit all of
/// it, including the address and customer comments, while the booking is Expired (§7.3).
/// </summary>
public sealed record CustomerDetails(
    string FirstName,
    string LastName,
    EmailAddress Email,
    MobileNumber MobileNumber,
    Address? Address = null,
    string? CustomerComments = null)
{
    public string FullName => $"{FirstName} {LastName}";
}
