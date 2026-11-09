namespace BergerHarbour.Application.Bookings.Dtos;

/// <summary>The details a customer entered online.</summary>
public sealed record CustomerDetailsDto(
    string FirstName,
    string LastName,
    string Email,
    string MobileNumber);
