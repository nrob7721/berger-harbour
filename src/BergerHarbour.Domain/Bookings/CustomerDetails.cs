using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>The contact details a customer entered online, snapshotted on the booking.</summary>
public sealed record CustomerDetails(string FirstName, string LastName, EmailAddress Email, MobileNumber MobileNumber)
{
    public string FullName => $"{FirstName} {LastName}";
}
