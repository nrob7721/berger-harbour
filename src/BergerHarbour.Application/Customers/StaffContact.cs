using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Customers;

/// <summary>The customer section of the staff booking dialog, validated.</summary>
public sealed record StaffContact(
    string FirstName,
    string LastName,
    EmailAddress Email,
    MobileNumber? Mobile,
    MobileNumber? SecondaryMobile,
    string? Address,
    string? Comments);
